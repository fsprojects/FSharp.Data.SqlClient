namespace FSharp.Data.Experimental.Internals

open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open System.Collections
open System.Diagnostics

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open FSharp.Data.Experimental

type QuotationsFactory private() = 
    
    //The entry point
    static member internal GetBody(methodName, specialization, [<ParamArray>] bodyFactoryArgs : obj[]) =
        
        let bodyFactory =   
            let mi = typeof<QuotationsFactory>.GetMethod(methodName, BindingFlags.NonPublic ||| BindingFlags.Static)
            assert(mi <> null)
            mi.MakeGenericMethod([| specialization |])

        fun(args : Expr list) -> 
            let sss = methodName
            let parameters = Array.append [| box args |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox

    //Core impl
    static member internal GetSqlCommandWithParamValuesSet(exprArgs : Expr list) = 
        <@
            let sqlCommand : SqlCommand = %%Expr.Coerce(exprArgs.[0], typeof<SqlCommand>)

            let paramValues : obj[] = %%Expr.NewArray(typeof<obj>, elements = [for x in exprArgs.Tail -> Expr.Coerce(x, typeof<obj>)])

            Debug.Assert(sqlCommand.Parameters.Count = paramValues.Length, "Expect size of values array to be equal to the number of SqlParameters.")
            for i = 0 to paramValues.Length - 1 do
                let p = sqlCommand.Parameters.[i]
                p.Value <- paramValues.[i]

            sqlCommand
        @>

    static member internal GetDataReader(exprArgs, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow  else CommandBehavior.Default 
        <@@ 
            async {
                let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet exprArgs
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

    static member internal GetRows(exprArgs, singleRow, includeColumnInfo) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%QuotationsFactory.GetDataReader(exprArgs, singleRow)
                let columnInfo = 
                    if includeColumnInfo 
                    then  
                        let table = reader.GetSchemaTable() 
                        if table = null then failwith "Metadata is not avialable in runtime"
                        table.Rows |> Seq.cast<DataRow> |> Seq.map(fun c->string c.["ColumnName"]) |> Array.ofSeq |> Some
                    else None
                return columnInfo, seq {
                    try 
                        while(not token.IsCancellationRequested && reader.Read()) do
                            let row = Array.zeroCreate reader.FieldCount
                            reader.GetValues(row) |> ignore
                            yield row  
                    finally
                        reader.Close()
                } 
            }
        @@>

    
    static member internal GetTypedSequenceWithCols<'Row>(exprArgs, rowMapper, singleRow, includeColumnInfo) = 
        let getTypedSeqAsync = 
            <@@
                async { 
                    let! (columns : string [] option, rows :  seq<obj[]>) = %%QuotationsFactory.GetRows(exprArgs, singleRow, includeColumnInfo)
                    return rows |> Seq.map<_,'Row> (fun row -> (%%rowMapper : string [] option * obj[] -> 'Row) (columns, row))
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
    
    static member internal GetTypedSequence<'Row>(exprArgs, rowMapper, singleRow, columns : Column list) = 
        let columnTypes, isNullableColumn = columns |> List.map (fun c -> c.ClrTypeFullName, c.IsNullable) |> List.unzip
        let combinedMapper = 
            <@@
                fun (_ : string [] option, x) -> 
                    (%%QuotationsFactory.MapObjectsToOptions(columnTypes, isNullableColumn) : obj[] -> unit) x
                    (%%rowMapper :  obj[] -> 'Row) x 
             @@>
        QuotationsFactory.GetTypedSequenceWithCols<'Row>(exprArgs, combinedMapper, singleRow, false)
        
    static member internal SelectOnlyColumn0<'Row>(exprArgs, singleRow, column : Column) = 
        QuotationsFactory.GetTypedSequence<'Row>(exprArgs, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ column ])

    static member internal GetMaps(exprArgs, singleRow) = 
        let combinedMapper = <@@ fun (cols : string [] option , x : obj[] )  -> Seq.zip cols.Value x |> Map.ofSeq @@>
        QuotationsFactory.GetTypedSequenceWithCols<Map<string,obj>>(exprArgs, combinedMapper, singleRow, true)

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(exprArgs, singleRow)  = 
        <@@
            async {
                use! reader = %%QuotationsFactory.GetDataReader(exprArgs, singleRow) : Async<SqlDataReader >
                let table = new DataTable<'T>() 
                table.Load reader
                return table
            }
        @@>

    //Utility methods            
    static member internal ToSqlParam(p : Parameter) = 
        let name = p.Name
        let dbType = p.TypeInfo.SqlDbTypeId
        <@@ 
            SqlParameter(
                name, 
                enum dbType, 
                Direction = %%Expr.Value p.Direction,
                TypeName = %%Expr.Value p.TypeInfo.UdttName
            )
        @@>
    
    static member internal MapArrayOptionItemToObj<'T>(arr, index) =
        <@
            let values : obj[] = %%arr
            values.[index] <- match unbox values.[index] with Some (x : 'T) -> box x | None -> null
        @> 

    static member internal MapArrayObjItemToOption<'T>(arr, index) =
        <@
            let values : obj[] = %%arr
            values.[index] <- box <| if Convert.IsDBNull(values.[index]) then None else Some(unbox<'T> values.[index])
        @> 

    static member internal MapArrayNullableItems(columnTypes : string list, isNullableColumn : bool list, mapper : string) = 
        assert(columnTypes.Length = isNullableColumn.Length)
        let arr = Var("_", typeof<obj[]>)
        let body =
            (columnTypes, isNullableColumn) 
            ||> List.zip
            |> List.mapi(fun index (typeName, isNullableColumn) ->
                if isNullableColumn 
                then 
                    typeof<QuotationsFactory>
                        .GetMethod(mapper, BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(Type.GetType typeName)
                        .Invoke(null, [| box(Expr.Var arr); box index |])
                        |> unbox
                        |> Some
                else 
                    None
            ) 
            |> List.choose id
            |> List.fold (fun acc x ->
                Expr.Sequential(acc, x)
            ) <@@ () @@>
        Expr.Lambda(arr, body)

    static member internal MapOptionsToObjects(columnTypes, isNullableColumn) = 
        QuotationsFactory.MapArrayNullableItems(columnTypes, isNullableColumn, "MapArrayOptionItemToObj")

    static member internal MapObjectsToOptions(columnTypes, isNullableColumn) = 
        QuotationsFactory.MapArrayNullableItems(columnTypes, isNullableColumn, "MapArrayObjItemToOption")

    static member internal GetNullableValueFromDataRow<'T>(exprArgs : Expr list, name : string) =
        <@
            let row : DataRow = %%exprArgs.[0]
            if row.IsNull name then None else Some(unbox<'T> row.[name])
        @> 

    static member internal SetNullableValueInDataRow<'T>(exprArgs : Expr list, name : string) =
        <@
            (%%exprArgs.[0] : DataRow).[name] <- match (%%exprArgs.[1] : option<'T>) with None -> null | Some value -> box value
        @> 

    static member internal MapExecuteArgs(executeArgs : Parameter list, argsExpr : Expr list) =
        assert(executeArgs.Length + 1 = argsExpr.Length)
        let sqlCommand = argsExpr.Head
        let args = 
            (argsExpr.Tail, executeArgs)
            ||> List.map2 (fun expr argInfo ->
                if argInfo.TypeInfo.TableType
                then
                    let columns = argInfo.TypeInfo.TvpColumns |> Seq.toList
                    let columnCount = columns.Length
                    assert (columnCount > 0)
                    let mapper = columns |> List.map (fun c -> c.ClrTypeFullName, c.IsNullable) |> List.unzip |> QuotationsFactory.MapOptionsToObjects 
                    <@@
                        let table = new DataTable()

                        for i = 0 to columnCount - 1 do
                            table.Columns.Add() |> ignore

                        let input : IEnumerable = %%Expr.Coerce(expr, typeof<IEnumerable>)
                        for row in input do
                            let values = if columnCount = 1 then [|box row|] else FSharpValue.GetTupleFields row
                            (%%mapper : obj[] -> unit) values
                            table.Rows.Add values |> ignore 
                            
                        table
                    @@>
                else
                    expr
            )   

        sqlCommand :: args




    

