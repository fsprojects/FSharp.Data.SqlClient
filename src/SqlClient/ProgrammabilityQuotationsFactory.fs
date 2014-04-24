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

type ProgrammabilityQuotationsFactory private() = 

    //The entry point
    static member internal GetBody(methodName, specialization, [<ParamArray>] bodyFactoryArgs : obj[]) =
        
        let bodyFactory =   
            let mi = typeof<ProgrammabilityQuotationsFactory>.GetMethod(methodName, BindingFlags.NonPublic ||| BindingFlags.Static)
            assert(mi <> null)
            mi.MakeGenericMethod([| specialization |])

        fun(args : Expr list) -> 
            let parameters = Array.append [| box args |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox

    //Core impl
    static member internal GetSqlCommandWithParamValuesSet(exprArgs : Expr list, paramInfos : Parameter list) = 
        assert(exprArgs.Length - 1 = paramInfos.Length)
        let mappedParamValues = 
            (exprArgs.Tail, paramInfos)
            ||> List.map2 (fun expr info ->
                if info.Direction = ParameterDirection.Input then
                    expr                    
                else 
                    typeof<QuotationsFactory>
                        .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(info.TypeInfo.ClrType)
                        .Invoke(null, [| box expr|])
                        |> unbox
            )

        let sqlParams = seq{ for p in paramInfos -> p.Name } |> String.concat ","

        <@
            let sqlCommand : SqlCommand = %%Expr.Coerce(exprArgs.[0], typeof<SqlCommand>)
            if sqlCommand.CommandType = CommandType.Text then
                sqlCommand.CommandText <- sprintf "SELECT * FROM %s(%s)" sqlCommand.CommandText sqlParams
            let xs = %%Expr.NewArray( typeof<SqlParameter>, paramInfos |> List.map QuotationsFactory.ToSqlParam)
            sqlCommand.Parameters.AddRange xs

            let paramValues : obj[] = %%Expr.NewArray(typeof<obj>, elements = [for x in mappedParamValues -> Expr.Coerce(x, typeof<obj>)])

            Debug.Assert(sqlCommand.Parameters.Count = paramValues.Length, "Expect size of values array to be equal to the number of SqlParameters.")
            for i = 0 to paramValues.Length - 1 do
                let p = sqlCommand.Parameters.[i]

                if not( p.SqlDbType = SqlDbType.Structured)
                then 
                    p.Value <- paramValues.[i]
                else
                    let table : DataTable = unbox p.Value
                    table.Rows.Clear()
                    for rowValues in unbox<seq<obj[]>> paramValues.[i] do
                        table.Rows.Add( rowValues) |> ignore

                if p.Value = DbNull 
                then 
                    match p.SqlDbType with
                    | SqlDbType.NVarChar -> p.Size <- 4000
                    | SqlDbType.VarChar -> p.Size <- 8000
                    | _ -> ()

            sqlCommand
        @>

    static member internal GetDataReader(exprArgs, paramInfos, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow  else CommandBehavior.Default 
        <@@ 
            async {
                let sqlCommand = %ProgrammabilityQuotationsFactory.GetSqlCommandWithParamValuesSet(exprArgs, paramInfos)
                //open connection async on .NET 4.5
                sqlCommand.Connection.Open()
                return!
                    try 
                        sqlCommand.AsyncExecuteReader(commandBehavior ||| CommandBehavior.CloseConnection ||| CommandBehavior.SingleResult)
                    with _ ->
                        sqlCommand.Connection.Close()
                        reraise()
            }
        @@>

    static member private GetRows<'Row>(exprArgs, paramInfos, mapper : Expr<(SqlDataReader -> 'Row)>, singleRow) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%ProgrammabilityQuotationsFactory.GetDataReader(exprArgs, paramInfos, singleRow)
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
    static member internal GetTypedSequence<'Row>(exprArgs, paramInfos, rowMapper, singleRow, columns : Column list) = 
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

        let getTypedSeqAsync = ProgrammabilityQuotationsFactory.GetRows(exprArgs, paramInfos, mapper, singleRow)
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
        
    static member internal SelectOnlyColumn0<'Row>(exprArgs, paramInfos, singleRow, column : Column) = 
        ProgrammabilityQuotationsFactory.GetTypedSequence<'Row>(exprArgs, paramInfos, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ column ])

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(exprArgs, paramInfos, singleRow)  = 
        <@@
            async {
                use! reader = %%ProgrammabilityQuotationsFactory.GetDataReader(exprArgs, paramInfos, singleRow) : Async<SqlDataReader >
                let table = new DataTable<'T>() 
                table.Load reader
                return table
            }
        @@>

    //Utility methods            
    static member private ObjToOption<'T> value = <@@ box <| if Convert.IsDBNull(%%value) then None else Some(unbox<'T> %%value) @@>  
 
    static member internal GetOutParameter (param : Parameter) =
        let paramName = param.Name
        let clrType = param.TypeInfo.ClrType
        let arr = Var("_", typeof<obj>)
        let body = typeof<ProgrammabilityQuotationsFactory>
                        .GetMethod("ObjToOption", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod([|clrType|])
                        .Invoke(null, [| box (Expr.Var arr) |])
                        |> unbox
        let converter = Expr.Lambda(arr, body)

        let needsConversion = clrType.IsValueType && param.Direction <> ParameterDirection.ReturnValue
        let resultType = if needsConversion then typedefof<_ option>.MakeGenericType clrType else clrType
        resultType, fun (args : Expr list) ->
        <@@ 
            let coll : SqlParameterCollection = %%Expr.Coerce(args.[0], typeof<SqlParameterCollection>)
            let param = coll |> Seq.cast<SqlParameter> |> Seq.find(fun p -> p.ParameterName = paramName)
            if needsConversion then
                let c : obj->obj = %%converter
                c param.Value
            elif param.Value = DbNull then null else param.Value
        @@>       
