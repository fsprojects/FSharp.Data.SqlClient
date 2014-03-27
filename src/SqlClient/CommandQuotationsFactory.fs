namespace FSharp.Data.Internals

open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open System.Collections
open System.Diagnostics

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open FSharp.Data

type CommandQuotationsFactory private() = 

    //The entry point
    static member internal GetBody(methodName, specialization, [<ParamArray>] bodyFactoryArgs : obj[]) =
        
        let bodyFactory =   
            let mi = typeof<CommandQuotationsFactory>.GetMethod(methodName, BindingFlags.NonPublic ||| BindingFlags.Static)
            assert(mi <> null)
            mi.MakeGenericMethod([| specialization |])

        fun(args : Expr list) -> 
            let parameters = Array.append [| box args |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox

    //Core impl
    static member internal GetSqlCommandWithParamValuesSet(exprArgs : Expr list, paramInfos : Parameter list, ?allParamsOptional) = 
        assert(exprArgs.Length - 1 = paramInfos.Length)
        let mappedParamValues = 
            if (defaultArg allParamsOptional false)
            then 
                (exprArgs.Tail, paramInfos)
                ||> List.map2 (fun expr info ->
                    if info.TypeInfo.ClrType.IsValueType
                    then 
                        typeof<QuotationsFactory>
                            .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                            .MakeGenericMethod(info.TypeInfo.ClrType)
                            .Invoke(null, [| box expr|])
                            |> unbox
                    else
                        expr
                )
            else
                exprArgs.Tail
        <@
            let sqlCommand : SqlCommand = %%Expr.Coerce(exprArgs.[0], typeof<SqlCommand>)

            let paramValues : obj[] = %%Expr.NewArray(typeof<obj>, elements = [for x in mappedParamValues -> Expr.Coerce(x, typeof<obj>)])

            Debug.Assert(sqlCommand.Parameters.Count = paramValues.Length, "Expect size of values array to be equal to the number of SqlParameters.")
            for i = 0 to paramValues.Length - 1 do
                let p = sqlCommand.Parameters.[i]
                p.Value <- paramValues.[i]
                if p.Value = null then p.Value <- DbNull
                if p.Value = DbNull && (p.SqlDbType = SqlDbType.NVarChar || p.SqlDbType = SqlDbType.VarChar)
                then p.Size <- if  p.SqlDbType = SqlDbType.NVarChar then 4000 else 8000
            sqlCommand
        @>

    static member internal GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow) = 
        <@@ 
            async {
                let sqlCommand = %CommandQuotationsFactory.GetSqlCommandWithParamValuesSet(exprArgs, paramInfos, allParametersOptional)
                //open connection async on .NET 4.5
                let connBehavior = 
                    if sqlCommand.Connection.State <> ConnectionState.Open then
                        //sqlCommand.Connection.StateChange.Add <| fun args -> printfn "Connection %i state change: %O -> %O" (sqlCommand.Connection.GetHashCode()) args.OriginalState args.CurrentState
                        sqlCommand.Connection.Open()
                        CommandBehavior.CloseConnection
                    else
                        CommandBehavior.Default 

                let overallBehavior = 
                    connBehavior
                    ||| (if singleRow then CommandBehavior.SingleRow else CommandBehavior.Default)
                    ||| CommandBehavior.SingleResult

                return!
                    try 
                        sqlCommand.AsyncExecuteReader( overallBehavior)
                    with _ ->
                        sqlCommand.Connection.Close()
                        reraise()
            }
        @@>

    static member private GetRows<'Row>(exprArgs, allParametersOptional, paramInfos, mapper : Expr<(SqlDataReader -> 'Row)>, singleRow) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%CommandQuotationsFactory.GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow)
                return seq {
                    try 
                        while(not token.IsCancellationRequested && reader.Read()) do
                            yield (%mapper) reader
                    finally
                        reader.Close()
                } 
            }
        @@>

    //API
    static member internal GetTypedSequence<'Row>(exprArgs, allParametersOptional, paramInfos, rowMapper, singleRow, columns : Column list) = 
        let columnTypes, isNullableColumn = columns |> List.map (fun c -> c.TypeInfo.ClrTypeFullName, c.IsNullable) |> List.unzip
        let mapper = 
            <@
                fun(reader : SqlDataReader) ->
                    let values = Array.zeroCreate columnTypes.Length
                    reader.GetValues(values) |> ignore
                    let mapNullables :  obj[] -> unit = %%QuotationsFactory.MapArrayNullableItems(columnTypes, isNullableColumn, "MapArrayObjItemToOption")
                    mapNullables values
                    (%%rowMapper : obj[] -> 'Row) values
            @>

        let getTypedSeqAsync = CommandQuotationsFactory.GetRows(exprArgs, allParametersOptional, paramInfos, mapper, singleRow)
        if singleRow
        then 
            <@@ 
                async { 
                    let! (xs : 'Row seq) = %%getTypedSeqAsync
                    return 
                        match List.ofSeq xs with
                        | [] -> None
                        | [ x ]  -> Some x 
                        | _ as ys -> raise <| InvalidOperationException(sprintf "Single row was expected but got %i." ys.Length)
                }
            @@>
        else
            getTypedSeqAsync
        
    static member internal SelectOnlyColumn0<'Row>(exprArgs, allParametersOptional, paramInfos, singleRow, column : Column) = 
        CommandQuotationsFactory.GetTypedSequence<'Row>(exprArgs, allParametersOptional, paramInfos, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ column ])

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(exprArgs, allParametersOptional, paramInfos, singleRow)  = 
        <@@
            async {
                use! reader = %%CommandQuotationsFactory.GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow) : Async<SqlDataReader >
                let table = new DataTable<'T>() 
                table.Load reader
                return table
            }
        @@>