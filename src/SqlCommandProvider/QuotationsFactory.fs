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
            let parameters = Array.append [| box args |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox

    //Core impl
    static member internal GetSqlCommandWithParamValuesSet(exprArgs : Expr list, allParametersOptional, paramInfos : Parameter list) = 
        assert(exprArgs.Length - 1 = paramInfos.Length)
        let mappedParamValues = 
            if not allParametersOptional
            then 
                exprArgs.Tail
            else
                let types = paramInfos |> List.map (fun p -> p.TypeInfo.ClrTypeFullName)
                (exprArgs.Tail, paramInfos)
                ||> List.map2 (fun expr info ->
                    typeof<QuotationsFactory>
                        .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(info.TypeInfo.ClrType)
                        .Invoke(null, [| box expr|])
                        |> unbox
                )

        <@
            let sqlCommand : SqlCommand = %%Expr.Coerce(exprArgs.[0], typeof<SqlCommand>)

            let paramValues : obj[] = %%Expr.NewArray(typeof<obj>, elements = [for x in mappedParamValues -> Expr.Coerce(x, typeof<obj>)])

            Debug.Assert(sqlCommand.Parameters.Count = paramValues.Length, "Expect size of values array to be equal to the number of SqlParameters.")
            for i = 0 to paramValues.Length - 1 do
                let p = sqlCommand.Parameters.[i]
                p.Value <- paramValues.[i]

            sqlCommand
        @>

    static member internal GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow  else CommandBehavior.Default 
        <@@ 
            async {
                let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet(exprArgs, allParametersOptional, paramInfos)
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

    static member internal GetRows<'Row>(exprArgs, allParametersOptional, paramInfos, mapper : Expr<(SqlDataReader -> 'Row)>, singleRow) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%QuotationsFactory.GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow)
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

        let getTypedSeqAsync = QuotationsFactory.GetRows(exprArgs, allParametersOptional, paramInfos, mapper, singleRow)
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
        QuotationsFactory.GetTypedSequence<'Row>(exprArgs, allParametersOptional, paramInfos, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ column ])

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(exprArgs, allParametersOptional, paramInfos, singleRow)  = 
        <@@
            async {
                use! reader = %%QuotationsFactory.GetDataReader(exprArgs, allParametersOptional, paramInfos, singleRow) : Async<SqlDataReader >
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
    
    static member internal OptionToObj<'T> value = <@@ match %%value with Some (x : 'T) -> box x | None -> SqlClient.DbNull @@>    

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
