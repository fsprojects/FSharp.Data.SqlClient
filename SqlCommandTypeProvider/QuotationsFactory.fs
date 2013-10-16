namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Reflection

open Microsoft.FSharp.Quotations

type QuotationsFactory private() = 
    
    static member internal GetDataReader(cmd, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow  else CommandBehavior.Default 
        <@@ 
            async {
                let sqlCommand : SqlCommand = %%Expr.Coerce(cmd, typeof<SqlCommand>)
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

    static member internal GetRows(cmd, singleRow, columnTypes : string list, isNullableColumn : bool list) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%QuotationsFactory.GetDataReader(cmd, singleRow)
                return seq {
                    try 
                        while(not token.IsCancellationRequested && reader.Read()) do
                            let row = Array.zeroCreate reader.VisibleFieldCount
                            reader.GetValues row |> ignore
                            for i = 0 to columnTypes.Length - 1 do
                                if isNullableColumn.[i]
                                then
                                    let t = typedefof<_ option>.MakeGenericType(Type.GetType columnTypes.[i])
                                    row.[i] <-  if row.[i] = null || reader.IsDBNull(i) 
                                                then t.GetMethod("get_None").Invoke(null, [||])
                                                else t.GetMethod("Some").Invoke(null, [| row.[i] |])
                            yield row  
                    finally
                        reader.Close()
                } |> Seq.cache
            }
        @@>

    static member internal GetTypedSequence<'Row>(cmd, rowMapper, singleRow, columnTypes : string list, isNullableColumn : bool list) = 
        assert (columnTypes.Length = isNullableColumn.Length)
        let nullValue = box System.DBNull.Value
        let getTypedSeqAsync = 
            <@@
                async { 
                    let! (rows : seq<obj[]>) = %%QuotationsFactory.GetRows(cmd, singleRow, columnTypes, isNullableColumn)
                    return rows |> Seq.map<_, 'Row> (%%rowMapper)                    
                }
            @@>

        if singleRow
        then 
            <@@ 
                async { 
                    let! xs  = %%getTypedSeqAsync : Async<'Row seq>
                    return Seq.exactlyOne xs
                }
            @@>
        else
            getTypedSeqAsync
            

    static member internal SelectOnlyColumn0<'Row>(cmd, singleRow, columntTypeName, isNullable) = 
        QuotationsFactory.GetTypedSequence<'Row>(cmd, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ columntTypeName ], [ isNullable])

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(cmd, singleRow)  = 
        <@@
            async {
                use! reader = %%QuotationsFactory.GetDataReader(cmd, singleRow) : Async<SqlDataReader >
                let table = new DataTable<'T>() 
                table.Load reader
                return table
            }
        @@>

    static member internal GetNullableValueFromRow<'T>(rowExpr, name : string) =
        <@
            let row : DataRow = %%rowExpr
            if row.IsNull name then None else Some(unbox<'T> row.[name])
        @> 

    static member internal SetNullableValueInRow<'T>(row : Expr, value : Expr, name : string) =
        <@
            (%%row : DataRow).[name] <- match (%%value : option<'T>) with None -> null | Some value -> box value
        @> 

    static member internal GetBody(methodName, specialization, [<ParamArray>] bodyFactoryArgs : obj[]) =

        let bodyFactory =   
            let mi = typeof<QuotationsFactory>.GetMethod(methodName, BindingFlags.NonPublic ||| BindingFlags.Static)
            assert(mi <> null)
            mi.MakeGenericMethod([| specialization |])

        fun(args : Expr list) -> 
            let parameters = Array.append [| for x in args -> box x |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox



    

