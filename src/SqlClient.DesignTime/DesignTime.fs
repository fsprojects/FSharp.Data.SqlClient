namespace FSharp.Data.SqlClient

open System
open System.Reflection
open System.Data
open System.Data.SqlClient
open Microsoft.SqlServer.Server
open System.Collections.Generic
open System.Diagnostics
open Microsoft.FSharp.Quotations
open ProviderImplementation.ProvidedTypes
open FSharp.Data.SqlClient.Internals
open System.Text.RegularExpressions

type internal RowType = {
    Provided: Type
    ErasedTo: Type
    Mapping: Expr
}

type internal ReturnType = {
    Single: Type
    PerRow: RowType option
}  with 
    member this.RowMapping = 
        match this.PerRow with
        | Some x -> x.Mapping
        | None -> Expr.Value Unchecked.defaultof<RowMapping> 
    member this.RowTypeName = 
        match this.PerRow with
        | Some x -> Expr.Value( x.ErasedTo.AssemblyQualifiedName)
        | None -> <@@ null: string @@>

type internal DataTableType =
| SqlProgrammabilityTable of isHostedExecution: bool * connectionString: DesignTimeConnectionString * schemaName: string * tableName: string * columns: Column list
| CommandResultTable 

module internal SharedLogic =
    /// Adds .Record or .Table inner type depending on resultType
    let alterReturnTypeAccordingToResultType (returnType: ReturnType) (cmdProvidedType: ProvidedTypeDefinition) resultType =
        if resultType = ResultType.Records then
            // Add .Record
            returnType.PerRow |> Option.iter (fun x -> 
                if x.Provided <> x.ErasedTo
                then cmdProvidedType.AddMember x.Provided
            )
        elif resultType = ResultType.DataTable then
            // add .Table
            returnType.Single |> function
            | :? ProvidedTypeDefinition -> cmdProvidedType.AddMember returnType.Single
            | _ -> ()

module Prefixes =
    let tempTable = "##SQLCOMMANDPROVIDER_"
    let tableVar = "@SQLCOMMANDPROVIDER_"

type DesignTime private() =
    static member internal AddGeneratedMethod
        (sqlParameters: Parameter list, hasOutputParameters, executeArgs: ProvidedParameter list, erasedType, providedOutputType, name) =

        let mappedInputParamValues (exprArgs: Expr list) = 
            (exprArgs.Tail, sqlParameters)
            ||> List.map2 (fun expr param ->
                let value = 
                    if param.Direction = ParameterDirection.Input
                    then 
                        if param.Optional && not param.TypeInfo.TableType 
                        then 
                            typeof<QuotationsFactory>
                                .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                                .MakeGenericMethod(param.TypeInfo.ClrType)
                                .Invoke(null, [| box expr|])
                                |> unbox
                        else
                            expr
                    else
                        let t = param.TypeInfo.ClrType

                        if t.IsArray
                        then Expr.Value(Array.CreateInstance(t.GetElementType(), param.Size))
                        elif t.IsPrimitive then
                            Expr.Value(Activator.CreateInstance(t), t)
                        else
                            Expr.DefaultValue(t)

                <@@ (%%Expr.Value(param.Name) : string), %%Expr.Coerce(value, typeof<obj>) @@>
            )

        let m = ProvidedMethod(name, executeArgs, providedOutputType, fun exprArgs ->
            let methodInfo = typeof<ISqlCommand>.GetMethod(name)
            let vals = mappedInputParamValues(exprArgs)
            let paramValues = Expr.NewArray( typeof<string * obj>, elements = vals)
            if not hasOutputParameters
            then 
                Expr.Call( Expr.Coerce( exprArgs.[0], erasedType), methodInfo, [ paramValues ])    
            else
                let mapOutParamValues = 
                    let arr = Var("parameters", typeof<(string * obj)[]>)
                    let body = 
                        (sqlParameters, exprArgs.Tail)
                        ||> List.zip
                        |> List.mapi (fun index (sqlParam, argExpr) ->
                            if sqlParam.Direction.HasFlag( ParameterDirection.Output)
                            then 
                                let mi = 
                                    ProvidedTypeBuilder.MakeGenericMethod(
                                        typeof<Mapper>
                                            .GetMethod("SetRef"),
                                            [sqlParam.TypeInfo.ClrType])
                                Expr.Call(mi, [ argExpr; Expr.Var arr; Expr.Value index ]) |> Some
                            else 
                                None
                        ) 
                        |> List.choose id
                        |> List.fold (fun acc x -> Expr.Sequential(acc, x)) <@@ () @@>

                    Expr.Lambda(arr, body)

                let xs = Var("parameters", typeof<(string * obj)[]>)
                let execute = Expr.Lambda(xs , Expr.Call( Expr.Coerce( exprArgs.[0], erasedType), methodInfo, [ Expr.Var xs ]))
                <@@
                    let ps: (string * obj)[] = %%paramValues
                    let result = (%%execute) ps
                    ps |> %%mapOutParamValues
                    result
                @@>
        )
        let xmlDoc = 
            sqlParameters
            |> Seq.choose (fun p ->
                if String.IsNullOrWhiteSpace p.Description
                then None
                else
                    let defaultConstrain = if p.DefaultValue.IsSome then sprintf " Default value: %O." p.DefaultValue.Value else ""
                    Some( sprintf "<param name='%s'>%O%s</param>" p.Name p.Description defaultConstrain)
            )
            |> String.concat "\n" 

        if not(String.IsNullOrWhiteSpace xmlDoc) then m.AddXmlDoc xmlDoc

        m

    static member internal GetRecordType(columns: Column list, unitsOfMeasurePerSchema) =
        
        columns 
            |> Seq.groupBy (fun x -> x.Name) 
            |> Seq.tryFind (fun (_, xs) -> Seq.length xs > 1)
            |> Option.iter (fun (name, _) -> failwithf "Non-unique column name %s is illegal for ResultType.Records." name)
        
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, hideObjectMethods = true)
        let properties, ctorParameters = 
            columns
            |> List.mapi ( fun i col ->
                let propertyName = col.Name

                if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." (i + 1)
                    
                let propType = col.GetProvidedType(unitsOfMeasurePerSchema)

                let property = ProvidedProperty(propertyName, propType, getterCode = fun args -> <@@ (unbox<DynamicRecord> %%args.[0]).[propertyName] @@>)

                let ctorParameter = ProvidedParameter(propertyName, propType)  

                property, ctorParameter
            )
            |> List.unzip

        recordType.AddMembers properties

        let ctor = ProvidedConstructor(ctorParameters, fun args ->
            let pairs =  Seq.zip args properties //Because we need original names in dictionary
                        |> Seq.map (fun (arg,p) -> <@@ (%%Expr.Value(p.Name):string), %%Expr.Coerce(arg, typeof<obj>) @@>)
                        |> List.ofSeq
            <@@
                let pairs : (string * obj) [] = %%Expr.NewArray(typeof<string * obj>, pairs)
                DynamicRecord (dict pairs)
            @@>
            )
        recordType.AddMember ctor
        
        recordType

    static member internal GetDataRowPropertyGetterAndSetterCode (column: Column) =
        let name = column.Name
        if column.Nullable then
            let getter = QuotationsFactory.GetBody("GetNullableValueFromDataRow", column.TypeInfo.ClrType, name)
            let setter = QuotationsFactory.GetBody("SetNullableValueInDataRow", column.TypeInfo.ClrType, name)
            getter, setter
        else
            let getter = QuotationsFactory.GetBody("GetNonNullableValueFromDataRow", column.TypeInfo.ClrType, name)
            let setter = QuotationsFactory.GetBody("SetNonNullableValueInDataRow", column.TypeInfo.ClrType, name)
            getter, setter

    static member internal GetDataRowType (columns: Column list, unitsOfMeasurePerSchema) = 
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)

        columns |> List.mapi(fun i col ->

            if col.Name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." (i + 1)

            let propertyType = col.GetProvidedType(unitsOfMeasurePerSchema)

            let getter, setter = DesignTime.GetDataRowPropertyGetterAndSetterCode col
            let property = ProvidedProperty(col.Name, propertyType, getterCode = getter, ?setterCode =
                                if not col.ReadOnly
                                then Some setter
                                else None)
            
            property
        )
        |> rowType.AddMembers

        rowType

    static member internal GetDataTableType(typeName, dataRowType: ProvidedTypeDefinition, outputColumns: Column list, dataTableType: DataTableType) =
        let tableType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ DataTable>, [ dataRowType ])
        let tableProvidedType = ProvidedTypeDefinition(typeName, Some tableType)
      
        let columnsType = ProvidedTypeDefinition("Columns", Some typeof<DataColumnCollection>)

        let columnsProperty = ProvidedProperty("Columns", columnsType, getterCode = fun args -> 
                <@@
                    let table : DataTable<DataRow> = %%args.[0]
                    table.Columns
                @@>)
        tableProvidedType.AddMember columnsType
        tableProvidedType.AddMember columnsProperty
      
        for column in outputColumns do
            let propertyType = ProvidedTypeDefinition(column.Name, Some typeof<DataColumn>)
            let property = ProvidedProperty(column.Name, propertyType, fun args -> 
                    let columnName = column.Name
                    <@@ 
                        let columns: DataColumnCollection = %%args.[0]
                        columns.[columnName]
                    @@>)
                        
            let getter, setter = DesignTime.GetDataRowPropertyGetterAndSetterCode(column)
            let getValueMethod =
                ProvidedMethod(
                    "GetValue"
                    , [ProvidedParameter("row", dataRowType)]
                    , column.ErasedToType
                    , fun args -> getter args.Tail // we don't care of args.[0] (the DataColumn) because getter code is already made for that column
            ) 
           
            let setValueMethod =
                ProvidedMethod(
                    "SetValue"
                    , [
                        ProvidedParameter("row", dataRowType)
                        ProvidedParameter("value", column.ErasedToType)
                    ]
                    , typeof<unit>
                    , fun args -> setter args.Tail  // we don't care of args.[0] (the DataColumn) because setter code is already made for that column
                )

            propertyType.AddMember getValueMethod
            propertyType.AddMember setValueMethod

            columnsType.AddMember property
            columnsType.AddMember propertyType


        let tableProperty =
            ProvidedProperty(
                "Table"
                , tableProvidedType
                , getterCode = 
                    fun args ->
                        <@@
                            let row : DataRow = %%args.[0]
                            let table = row.Table
                            table
                        @@>
            )
        dataRowType.AddMember tableProperty

        let getColumnsSerializedSchema columns =
          let columns = List.toArray columns 
          Encoding.serialize columns

        let ctorCode =
            fun _ ->
                let serializedSchema = getColumnsSerializedSchema outputColumns
                match dataTableType with
                | SqlProgrammabilityTable (isHostedExecution, connectionString, schemaName, tableName, _) ->
                    
                    let twoPartTableName = sprintf "[%s].[%s]" schemaName tableName 

                    <@@ 
                        let connectionString = lazy %%connectionString.RunTimeValueExpr(isHostedExecution)
                        let selectCommand = new SqlCommand("SELECT * FROM " + twoPartTableName)
                        let table = new DataTable<DataRow>(selectCommand, connectionString)
                        table.TableName <- twoPartTableName
                        RuntimeInternals.setupTableFromSerializedColumns serializedSchema table
                        table
                    @@>
                | CommandResultTable ->
                    <@@ 
                        let table = new DataTable<DataRow>(null, null)
                        RuntimeInternals.setupTableFromSerializedColumns serializedSchema table
                        table
                    @@>
          

        ProvidedConstructor([], invokeCode = ctorCode) |> tableProvidedType.AddMember

        tableProvidedType

    static member internal GetOutputTypes (outputColumns: Column list, resultType, rank: ResultRank, hasOutputParameters, unitsOfMeasurePerSchema) =    
        if resultType = ResultType.DataReader 
        then 
            { Single = typeof<SqlDataReader>; PerRow = None }
        elif outputColumns.IsEmpty
        then 
            { Single = typeof<int>; PerRow = None }
        elif resultType = ResultType.DataTable 
        then
            let dataRowType = DesignTime.GetDataRowType(outputColumns, unitsOfMeasurePerSchema)
            let dataTableType = DesignTime.GetDataTableType("Table", dataRowType, outputColumns, CommandResultTable)
            dataTableType.AddMember dataRowType

            { Single = dataTableType; PerRow = None }

        else 
            let providedRowType, erasedToRowType, rowMapping = 
                if List.length outputColumns = 1
                then
                    let column0 = outputColumns.Head
                    let erasedTo = column0.ErasedToType
                    let provided = column0.GetProvidedType(unitsOfMeasurePerSchema)
                    let values = Var("values", typeof<obj[]>)
                    let indexGet = Expr.Call(Expr.Var values, typeof<Array>.GetMethod("GetValue",[|typeof<int>|]), [Expr.Value 0])
                    provided, erasedTo, Expr.Lambda(values,  indexGet) 

                elif resultType = ResultType.Records 
                then 
                    let provided = DesignTime.GetRecordType(outputColumns, unitsOfMeasurePerSchema)
                    let names = Expr.NewArray(typeof<string>, outputColumns |> List.map (fun x -> Expr.Value(x.Name))) 
                    let mapping = 
                        <@@ 
                            fun (values: obj[]) -> 
                                let data = Dictionary()
                                let names: string[] = %%names
                                for i = 0 to names.Length - 1 do 
                                    data.Add(names.[i], values.[i])
                                DynamicRecord( data) |> box 
                        @@>

                    upcast provided, typeof<obj>, mapping
                else 
                    let erasedToTupleType = 
                        match outputColumns with
                        | [ x ] -> x.ErasedToType
                        | xs -> Microsoft.FSharp.Reflection.FSharpType.MakeTupleType [| for x in xs -> x.ErasedToType |]

                    let providedType = 
                        match outputColumns with
                        | [ x ] -> x.GetProvidedType(unitsOfMeasurePerSchema)
                        | xs -> Microsoft.FSharp.Reflection.FSharpType.MakeTupleType [| for x in xs -> x.GetProvidedType(unitsOfMeasurePerSchema) |]

                    let clrTypeName = erasedToTupleType.FullName
                    let mapping = <@@ Microsoft.FSharp.Reflection.FSharpValue.PreComputeTupleConstructor (Type.GetType(clrTypeName, throwOnError = true))  @@>
                    providedType, erasedToTupleType, mapping
            
            let nullsToOptions = QuotationsFactory.MapArrayNullableItems(outputColumns, "MapArrayObjItemToOption") 
            let combineWithNullsToOptions = typeof<Mapper>.GetMethod("GetMapperWithNullsToOptions") 
            
            { 
                Single = 
                    match rank with
                    | ResultRank.ScalarValue -> providedRowType
                    | ResultRank.SingleRow -> ProvidedTypeBuilder.MakeGenericType(typedefof<_ option>, [ providedRowType ])
                    | ResultRank.Sequence -> 
                        let collectionType = if hasOutputParameters then typedefof<_ list> else typedefof<_ seq>
                        ProvidedTypeBuilder.MakeGenericType( collectionType, [ providedRowType ])
                    | unexpected -> failwithf "Unexpected ResultRank value: %A" unexpected

                PerRow = Some { 
                    Provided = providedRowType
                    ErasedTo = erasedToRowType
                    Mapping = Expr.Call( combineWithNullsToOptions, [ nullsToOptions; rowMapping ]) 
                }               
            }

    static member internal GetOutputColumns (connection: SqlConnection, commandText, parameters: Parameter list, isStoredProcedure) = 
        try
            connection.GetFullQualityColumnInfo(commandText) 
        with :? SqlException as why ->
            try 
                let commandType = if isStoredProcedure then CommandType.StoredProcedure else CommandType.Text
                connection.FallbackToSETFMONLY(commandText, commandType, parameters) 
            with :? SqlException ->
                raise why

    static member internal ParseParameterInfo(cmd: SqlCommand) = 
        cmd.ExecuteQuery(fun cursor ->
            string cursor.["name"], 
            unbox<int> cursor.["suggested_system_type_id"], 
            cursor.TryGetValue "suggested_user_type_id",
            unbox cursor.["suggested_is_output"],
            unbox cursor.["suggested_is_input"],
            cursor.["suggested_max_length"] |> unbox<int16> |> int,
            unbox cursor.["suggested_precision"] |> unbox<byte>,
            unbox cursor.["suggested_scale"] |> unbox<byte>
        )        

    static member internal ExtractParameters(connection, commandText: string, allParametersOptional) =  
        
        use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", connection, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore

        let parameters = 
            try
                DesignTime.ParseParameterInfo( cmd) |> Seq.toArray
            with 
                | :? SqlException as why when why.Class = 16uy && why.Number = 11508 && why.State = 1uy && why.ErrorCode = -2146232060 ->
                    match DesignTime.RewriteSqlStatementToEnableMoreThanOneParameterDeclaration(cmd, why) with
                    | Some x -> x
                    | None -> reraise()
                | _ -> 
                    reraise()

        parameters
        |> Seq.map(fun (name, sqlEngineTypeId, userTypeId, is_output, is_input, max_length, precision, scale) ->
            let direction = 
                if is_output
                then 
                    invalidArg name "Output parameters are not supported"
                else 
                    assert(is_input)
                    ParameterDirection.Input 
                    
            let typeInfo = findTypeInfoBySqlEngineTypeId(connection.ConnectionString, sqlEngineTypeId, userTypeId)

            { 
                Name = name
                TypeInfo = typeInfo 
                Direction = direction 
                MaxLength = max_length 
                Precision = precision 
                Scale = scale 
                DefaultValue = None
                Optional = allParametersOptional 
                Description = null 
            }
        )
        |> Seq.toList

    static member internal RewriteSqlStatementToEnableMoreThanOneParameterDeclaration(cmd: SqlCommand, why: SqlException) =  
        
        let getVariables tsql = 
            let parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql140Parser( true)
            let tsqlReader = new System.IO.StringReader(tsql)
            let errors = ref Unchecked.defaultof<_>
            let fragment = parser.Parse(tsqlReader, errors)

            let allVars = ResizeArray()
            let declaredVars = ResizeArray()

            fragment.Accept {
                new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragmentVisitor() with
                    member __.Visit(node : Microsoft.SqlServer.TransactSql.ScriptDom.VariableReference) = 
                        base.Visit node
                        allVars.Add(node.Name, node.StartOffset, node.FragmentLength)
                    member __.Visit(node : Microsoft.SqlServer.TransactSql.ScriptDom.DeclareVariableElement) = 
                        base.Visit node
                        declaredVars.Add(node.VariableName.Value)
            }
            let unboundVars = 
                allVars 
                |> Seq.groupBy (fun (name, _, _)  -> name)
                |> Seq.choose (fun (name, xs) -> 
                    if declaredVars.Contains name 
                    then None 
                    else Some(name, xs |> Seq.mapi (fun i (_, start, length) -> sprintf "%s%i" name i, start, length)) 
                )
                |> dict

            unboundVars, !errors

        let mutable tsql = cmd.Parameters.["@tsql"].Value.ToString()
        let unboundVars, parseErrors = getVariables tsql
        if parseErrors.Count = 0
        then 
            let usedMoreThanOnceVariable = 
                why.Message.Replace("The undeclared parameter '", "").Replace("' is used more than once in the batch being analyzed.", "")
            Debug.Assert(
                unboundVars.Keys.Contains( usedMoreThanOnceVariable), 
                sprintf "Could not find %s among extracted unbound vars: %O" usedMoreThanOnceVariable (List.ofSeq unboundVars.Keys)
            )
            let mutable startAdjustment = 0
            for xs in unboundVars.Values do
                for newName, start, len in xs do
                    let before = tsql
                    let start = start + startAdjustment
                    let after = before.Remove(start, len).Insert(start, newName)
                    tsql <- after
                    startAdjustment <- startAdjustment + (after.Length - before.Length)
            cmd.Parameters.["@tsql"].Value <- tsql
            let altered = DesignTime.ParseParameterInfo cmd
            let mapBack = unboundVars |> Seq.collect(fun (KeyValue(name, xs)) -> [ for newName, _, _ in xs -> newName, name ]) |> dict
            let tryUnify = 
                altered
                |> Seq.map (fun (name, sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input, max_length, precision, scale) -> 
                    let oldName = 
                        match mapBack.TryGetValue name with 
                        | true, original -> original 
                        | false, _ -> name
                    oldName, (sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input, max_length, precision, scale)
                )
                |> Seq.groupBy fst
                |> Seq.map( fun (name, xs) -> name, xs |> Seq.map snd |> Seq.distinct |> Seq.toArray)
                |> Seq.toArray

            if tryUnify |> Array.exists( fun (_, xs) -> xs.Length > 1)
            then 
                None
            else
                tryUnify 
                |> Array.map (fun (name, xs) -> 
                    let sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input, max_length, precision, scale = xs.[0] //|> Seq.exactlyOne
                    name, sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input, max_length, precision, scale
                )
                |> Some
        else
            None

    static member internal CreateUDTT(t: TypeInfo, unitsOfMeasurePerSchema) = 
        assert(t.TableType)
        let rowType = ProvidedTypeDefinition(t.UdttName, Some typeof<obj>, hideObjectMethods = true)

        let parameters, sqlMetas = 
            List.unzip [ 
                for p in t.TableTypeColumns do
                    let name = p.Name
                    let param = ProvidedParameter( name, p.GetProvidedType(unitsOfMeasurePerSchema), ?optionalValue = if p.Nullable then Some null else None) 
                    let sqlMeta =
                        let typeInfo = p.GetTypeInfoConsideringUDDT()
                        let dbType = typeInfo.SqlDbType
                        let precision = byte p.Precision
                        let scale = byte p.Scale
                        if typeInfo.IsFixedLength then
                            if scale = 0uy then
                                <@@ SqlMetaData(name, dbType) @@>
                            else
                                <@@ SqlMetaData(name, dbType, precision, scale) @@>
                        else 
                            let maxLength = int64 p.MaxLength
                            <@@ SqlMetaData(name, dbType, maxLength) @@>
                    yield param, sqlMeta
            ] 

        let ctor = ProvidedConstructor( parameters, fun args -> 
            let optionsToNulls = QuotationsFactory.MapArrayNullableItems(List.ofArray t.TableTypeColumns, "MapArrayOptionItemToObj") 

            <@@
                let values: obj[] = %%Expr.NewArray(typeof<obj>, [ for a in args -> Expr.Coerce(a, typeof<obj>) ])
                (%%optionsToNulls) values

                //let record = new SqlDataRecord()
                //record.SetValues(values) |> ignore

                //done via reflection because not implemented on Mono
                let sqlDataRecordType = typeof<SqlCommand>.Assembly.GetType("Microsoft.SqlServer.Server.SqlDataRecord", throwOnError = true)
                let record = Activator.CreateInstance(sqlDataRecordType, args = [| %%Expr.Coerce(Expr.NewArray(typeof<SqlMetaData>, sqlMetas), typeof<obj>) |])
                sqlDataRecordType.GetMethod("SetValues").Invoke(record, [| values |]) |> ignore

                record
            @@>)

        rowType.AddMember ctor
        rowType.AddXmlDoc "User-Defined Table Type"
                            
        rowType

                
    static member internal GetExecuteArgs(cmdProvidedType: ProvidedTypeDefinition, sqlParameters: Parameter list, udttsPerSchema: IDictionary<_, ProvidedTypeDefinition>, unitsOfMeasurePerSchema) = 
        [
            for p in sqlParameters do
                assert p.Name.StartsWith("@")
                let parameterName = p.Name.Substring 1

                yield 
                    if not p.TypeInfo.TableType 
                    then
                        if p.Optional 
                        then 
                            assert(p.Direction = ParameterDirection.Input)
                            ProvidedParameter(parameterName, parameterType = typedefof<_ option>.MakeGenericType(p.TypeInfo.ClrType))
                        else
                            if p.Direction.HasFlag(ParameterDirection.Output)
                            then
                                ProvidedParameter(parameterName, parameterType = p.TypeInfo.ClrType.MakeByRefType(), isOut = true)
                            else                                 
                                ProvidedParameter(parameterName, parameterType = p.GetProvidedType(unitsOfMeasurePerSchema), ?optionalValue = p.DefaultValue)
                    else
                        assert(p.Direction = ParameterDirection.Input)

                        let userDefinedTableTypeRow = 
                            if udttsPerSchema = null
                            then //SqlCommandProvider case
                                match cmdProvidedType.GetNestedType(p.TypeInfo.UdttName) with 
                                | null -> 
                                    let rowType = DesignTime.CreateUDTT(p.TypeInfo, unitsOfMeasurePerSchema)
                                    cmdProvidedType.AddMember rowType
                                    rowType
                                | x -> downcast x //same type appears more than once
                            else //SqlProgrammability
                                let udtt = udttsPerSchema.[p.TypeInfo.Schema].GetNestedType(p.TypeInfo.UdttName)
                                downcast udtt

                        ProvidedParameter(
                            parameterName, 
                            parameterType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ userDefinedTableTypeRow ])
                        )

        ]

    static member internal GetCommandCtors(cmdProvidedType: ProvidedTypeDefinition, designTimeConfig, (designTimeConnectionString:DesignTimeConnectionString), isHostedExecution, ?factoryMethodName) = 
        [
            let ctorImpl = typeof<``ISqlCommand Implementation``>.GetConstructor [| typeof<DesignTimeConfig>; typeof<Connection>; typeof<int> |]

            let parameters1 = [ 
                ProvidedParameter("connectionString", typeof<string>) 
                ProvidedParameter("commandTimeout", typeof<int>, optionalValue = SqlCommand.DefaultTimeout) 
            ]

            let body1 (args: _ list) = 
                Expr.NewObject(ctorImpl, designTimeConfig :: <@@ Connection.Choice1Of3 %%args.Head @@> :: args.Tail)

            yield ProvidedConstructor(parameters1, invokeCode = body1) :> MemberInfo
            
            if factoryMethodName.IsSome
            then 
                yield upcast ProvidedMethod(factoryMethodName.Value, parameters1, returnType = cmdProvidedType, isStatic = true, invokeCode = body1)
           
            let parameters2 = 
                    [ 
                        ProvidedParameter(
                            "connection", 
                            typeof<SqlConnection>,
                            ?optionalValue = if designTimeConnectionString.IsDefinedByLiteral then None else Some null
                        )           
                        ProvidedParameter("transaction", typeof<SqlTransaction>, optionalValue = null) 
                        ProvidedParameter("commandTimeout", typeof<int>, optionalValue = SqlCommand.DefaultTimeout) 
                    ]

            let connectionStringExpr = designTimeConnectionString.RunTimeValueExpr(isHostedExecution)
            let body2 (args: _ list) =
                let connArg = 
                    <@@ 
                        if box (%%args.[1]: SqlTransaction) <> null 
                        then Connection.Choice3Of3 %%args.[1]
                        elif box (%%args.[0]: SqlConnection) <> null 
                        then Connection.Choice2Of3 %%args.Head 
                        else Connection.Choice1Of3( %%connectionStringExpr)
                    @@>
                Expr.NewObject(ctorImpl, [ designTimeConfig ; connArg; args.[2] ])
                    
            yield upcast ProvidedConstructor(parameters2, invokeCode = body2)
            if factoryMethodName.IsSome
            then 
                yield upcast ProvidedMethod(factoryMethodName.Value, parameters2, returnType = cmdProvidedType, isStatic = true, invokeCode = body2)
        ]

    static member private CreateTempTableRecord(name, cols, unitsOfMeasurePerSchema) =
        let rowType = ProvidedTypeDefinition(name, Some typeof<obj>, hideObjectMethods = true)

        let parameters =
            [
                for (p : Column) in cols do
                    let name = p.Name
                    let param = ProvidedParameter( name, p.GetProvidedType(unitsOfMeasurePerSchema), ?optionalValue = if p.Nullable then Some null else None)
                    yield param
            ]

        let ctor = ProvidedConstructor( parameters, fun args ->
            let optionsToNulls = QuotationsFactory.MapArrayNullableItems(cols, "MapArrayOptionItemToObj")

            <@@ let values: obj[] = %%Expr.NewArray(typeof<obj>, [ for a in args -> Expr.Coerce(a, typeof<obj>) ])
                (%%optionsToNulls) values
                values @@>)

        rowType.AddMember ctor
        rowType.AddXmlDoc "Type Table Type"

        rowType

    // Changes any temp tables in to a global temp table (##name) then creates them on the open connection.
    static member internal SubstituteTempTables(connection, commandText: string, tempTableDefinitions : string, connectionId, unitsOfMeasurePerSchema) =
        // Extract and temp tables
        let tempTableRegex = Regex("#([a-z0-9\-_]+)", RegexOptions.IgnoreCase)
        let tempTableNames =
            tempTableRegex.Matches(tempTableDefinitions)
            |> Seq.cast<Match>
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Seq.toList

        match tempTableNames with
        | [] -> commandText, None
        | _ ->
            // Create temp table(s), extracts the columns then drop it.
            let tableTypes =
                use create = new SqlCommand(tempTableDefinitions, connection)
                create.ExecuteScalar() |> ignore

                tempTableNames
                |> List.map(fun name ->
                    let cols = DesignTime.GetOutputColumns(connection, "SELECT * FROM #"+name, [], isStoredProcedure = false)
                    use drop = new SqlCommand("DROP TABLE #"+name, connection)
                    drop.ExecuteScalar() |> ignore
                    DesignTime.CreateTempTableRecord(name, cols, unitsOfMeasurePerSchema), cols)

            let parameters =
                tableTypes
                |> List.map (fun (typ, _) ->
                    ProvidedParameter(typ.Name, parameterType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ typ ])))

            // Build the values load method.
            let loadValues (exprArgs: Expr list) (connection) =
                (exprArgs.Tail, tableTypes)
                ||> List.map2 (fun expr (typ, cols) ->
                    let destinationTableName = typ.Name
                    let colsLength = cols.Length

                    <@@
                        let items = (%%expr : obj seq)
                        use reader = new TempTableLoader(colsLength, items)

                        use bulkCopy = new SqlBulkCopy((%%connection : SqlConnection))
                        bulkCopy.BulkCopyTimeout <- 0
                        bulkCopy.BatchSize <- 5000
                        bulkCopy.DestinationTableName <- "#" + destinationTableName
                        bulkCopy.WriteToServer(reader)

                         @@>
                )
                |> List.fold (fun acc x -> Expr.Sequential(acc, x)) <@@ () @@>

            let loadTempTablesMethod = ProvidedMethod("LoadTempTables", parameters, typeof<unit>, fun exprArgs ->
                let command = Expr.Coerce(exprArgs.[0], typedefof<ISqlCommand>)

                let connection =
                    <@@ let cmd = (%%command : ISqlCommand)
                        cmd.Raw.Connection @@>

                <@@ do
                        use create = new SqlCommand(tempTableDefinitions, (%%connection : SqlConnection))
                        create.ExecuteNonQuery() |> ignore

                    (%%loadValues exprArgs connection)
                    ignore() @@>)

            // Create the temp table(s) but as a global temp table with a unique name. This can be used later down stream on the open connection.
            use cmd = new SqlCommand(tempTableRegex.Replace(tempTableDefinitions, Prefixes.tempTable+connectionId+"$1"), connection)
            cmd.ExecuteScalar() |> ignore

            // Only replace temp tables we find in our list.
            tempTableRegex.Replace(commandText, MatchEvaluator(fun m ->
                match tempTableNames |> List.tryFind((=) m.Groups.[1].Value) with
                | Some name -> Prefixes.tempTable + connectionId + name
                | None -> m.Groups.[0].Value)),

            Some(loadTempTablesMethod, tableTypes |> List.unzip |> fst)

    static member internal RemoveSubstitutedTempTables(connection, tempTables : ProvidedTypeDefinition list, connectionId) =
        if not tempTables.IsEmpty then
            use cmd = new SqlCommand(tempTables |> List.map(fun tempTable -> sprintf "DROP TABLE [%s%s%s]" Prefixes.tempTable connectionId tempTable.Name) |> String.concat ";", connection)
            cmd.ExecuteScalar() |> ignore

    // tableVarMapping(s) is converted into DECLARE statements then prepended to the command text.
    static member internal SubstituteTableVar(commandText: string, tableVarMapping : string) =
        let varRegex = Regex("@([a-z0-9_]+)", RegexOptions.IgnoreCase)

        let vars =
            tableVarMapping.Split([|';'|], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose(fun (x : string) ->
                match x.Split([|'='|]) with
                | [|name;typ|] -> Some(name.TrimStart('@'), typ)
                | _ ->  None)

        // Only replace table vars we find in our list.
        let commandText =
            varRegex.Replace(commandText, MatchEvaluator(fun m ->
                match vars |> Array.tryFind(fun (n,_) -> n = m.Groups.[1].Value) with
                | Some (name, _) -> Prefixes.tableVar + name
                | None -> m.Groups.[0].Value))

        (vars |> Array.map(fun (name,typ) -> sprintf "DECLARE %s%s %s = @%s" Prefixes.tableVar name typ name) |> String.concat "; ") + "; " + commandText
