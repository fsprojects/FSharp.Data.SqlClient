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
open FSharp.Data.Experimental.Runtime

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
    static member internal GetSqlCommandWithParamValuesSet(exprArgs : Expr list, paramInfos : Parameter list) = 
        assert(exprArgs.Length - 1 = paramInfos.Length)
        let mappedParamValues = 
            (exprArgs.Tail, paramInfos)
            ||> List.map2 (fun expr info ->
                if info.Direction <> ParameterDirection.Input then
                    typeof<QuotationsFactory>
                        .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(info.TypeInfo.ClrType)
                        .Invoke(null, [| box expr|])
                        |> unbox
                else 
                    expr
            )

        <@
            let sqlCommand : SqlCommand = %%Expr.Coerce(exprArgs.[0], typeof<SqlCommand>)
            let xs = %%Expr.NewArray( typeof<SqlParameter>, paramInfos |> List.map QuotationsFactory.ToSqlParam)
            sqlCommand.Parameters.AddRange xs

            let paramValues : obj[] = %%Expr.NewArray(typeof<obj>, elements = [for x in mappedParamValues -> Expr.Coerce(x, typeof<obj>)])

            Debug.Assert(sqlCommand.Parameters.Count = paramValues.Length, "Expect size of values array to be equal to the number of SqlParameters.")
            for i = 0 to paramValues.Length - 1 do
                let p = sqlCommand.Parameters.[i]
                p.Value <- paramValues.[i]
                if p.Value = DbNull && (p.SqlDbType = SqlDbType.NVarChar || p.SqlDbType = SqlDbType.VarChar)
                then p.Size <- if  p.SqlDbType = SqlDbType.NVarChar then 4000 else 8000
            sqlCommand
        @>

    static member internal GetDataReader(exprArgs, paramInfos, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow  else CommandBehavior.Default 
        <@@ 
            async {
                let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet(exprArgs, paramInfos)
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

    static member internal GetRows<'Row>(exprArgs, paramInfos, mapper : Expr<(SqlDataReader -> 'Row)>, singleRow) = 
        <@@ 
            async {
                let! token = Async.CancellationToken
                let! (reader : SqlDataReader) = %%QuotationsFactory.GetDataReader(exprArgs, paramInfos, singleRow)
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

        let getTypedSeqAsync = QuotationsFactory.GetRows(exprArgs, paramInfos, mapper, singleRow)
        if singleRow
        then 
            <@@ 
                async { 
                    let! (xs : 'Row seq) = %%getTypedSeqAsync
                    return Seq.exactlyOne xs
                }
            @@>
        else
            getTypedSeqAsync
        
    static member internal SelectOnlyColumn0<'Row>(exprArgs, paramInfos, singleRow, column : Column) = 
        QuotationsFactory.GetTypedSequence<'Row>(exprArgs, paramInfos, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow, [ column ])

    static member internal GetTypedDataTable<'T when 'T :> DataRow>(exprArgs, paramInfos, singleRow)  = 
        <@@
            async {
                use! reader = %%QuotationsFactory.GetDataReader(exprArgs, paramInfos, singleRow) : Async<SqlDataReader >
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
            let r = SqlParameter(
                        name, 
                        enum dbType, 
                        Direction = %%Expr.Value p.Direction,
                        TypeName = %%Expr.Value p.TypeInfo.UdttName
                    )
            if %%Expr.Value p.TypeInfo.SqlEngineTypeId = 240 then
                r.UdtTypeName <- %%Expr.Value p.TypeInfo.TypeName
            r
        @@>
    
    static member internal GetOutParameter (param : Parameter) =
        let paramName = param.Name
        let clrType = param.TypeInfo.ClrType
        let arr = Var("_", typeof<obj>)
        let body = typeof<QuotationsFactory>
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

    static member internal OptionToObj<'T> value = <@@ match %%value with Some (x : 'T) -> box x | None -> DbNull @@>    

    static member internal ObjToOption<'T> value = 
        <@@ 
            let result = if Convert.IsDBNull(%%value) then None else Some(unbox<'T> %%value)
            box result 
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
