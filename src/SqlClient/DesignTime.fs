namespace FSharp.Data.SqlClient

open System
open System.Reflection
open System.Data
open System.Data.SqlClient
open System.Collections.Generic
open System.Diagnostics
open Microsoft.FSharp.Quotations
//open Microsoft.FSharp.Reflection
open ProviderImplementation.ProvidedTypes
open FSharp.Data

type internal ResultTypes = {
    ProvidedType : Type
    ErasedToType : Type
    ProvidedRowType : ProvidedTypeDefinition option
    ErasedToRowType : Type 
    RowMapping : Expr
}   with

    static member SingleTypeResult(provided, ?erasedTo)  = { 
        ProvidedType = provided
        ErasedToType = defaultArg erasedTo provided
        ProvidedRowType = None
        ErasedToRowType = typeof<Void>
        RowMapping = Expr.Value Unchecked.defaultof<RowMapping> 
    }

type DesignTime private() = 
    static member internal AddGeneratedMethod
        (sqlParameters: Parameter list, executeArgs: ProvidedParameter list, erasedType, providedOutputType, name) =

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
                        Expr.Value(Activator.CreateInstance(t), t)

                <@@ (%%Expr.Value(param.Name) : string), %%Expr.Coerce(value, typeof<obj>) @@>
            )

        let m = ProvidedMethod(name, executeArgs, providedOutputType)
        
        m.InvokeCode <- fun exprArgs ->
            let methodInfo = typeof<ISqlCommand>.GetMethod(name)
            let vals = mappedInputParamValues(exprArgs)
            let paramValues = Expr.NewArray( typeof<string * obj>, elements = vals)
            let inputParametersOnly = sqlParameters |> List.forall (fun x -> x.Direction = ParameterDirection.Input)
            if inputParametersOnly
            then 
                Expr.Call( Expr.Coerce( exprArgs.[0], erasedType), methodInfo, [ paramValues ])    
            else
                let mapOutParamValues = 
                    let arr = Var("parameters", typeof<(string * obj)[]>)
                    let body = 
                        (sqlParameters, exprArgs.Tail)
                        ||> List.zip
                        |> List.mapi (fun index (sqlParam, argExpr) ->
                            if sqlParam.Direction = ParameterDirection.Output
                            then 
                                let mi = 
                                    typeof<DesignTime>
                                        .GetMethod("SetRef")
                                        .MakeGenericMethod( sqlParam.TypeInfo.ClrType)
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

    static member SetRef<'t>(r : byref<'t>, arr: (string * obj)[], i) = 
        r <- arr.[i] |> snd |> unbox

    static member internal GetRecordType(columns: Column list) =
        
        columns 
            |> Seq.groupBy (fun x -> x.Name) 
            |> Seq.tryFind (fun (_, xs) -> Seq.length xs > 1)
            |> Option.iter (fun (name, _) -> failwithf "Non-unique column name %s is illegal for ResultType.Records." name)
        
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        let properties, ctorParameters = 
            columns
            |> List.mapi ( fun i col ->
                let propertyName = col.Name

                if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." (i + 1)
                    
                let propType = col.ClrTypeConsideringNullable

                let property = ProvidedProperty(propertyName, propType)
                property.GetterCode <- fun args -> <@@ (unbox<DynamicRecord> %%args.[0]).[propertyName] @@>

                let ctorParameter = ProvidedParameter(propertyName, propType)  

                property, ctorParameter
            )
            |> List.unzip

        recordType.AddMembers properties

        let ctor = ProvidedConstructor(ctorParameters)
        ctor.InvokeCode <- fun args ->
            let pairs =  Seq.zip args properties //Because we need original names in dictionary
                        |> Seq.map (fun (arg,p) -> <@@ (%%Expr.Value(p.Name):string), %%Expr.Coerce(arg, typeof<obj>) @@>)
                        |> List.ofSeq
            <@@
                let pairs : (string * obj) [] = %%Expr.NewArray(typeof<string * obj>, pairs)
                DynamicRecord (dict pairs)
            @@> 
        recordType.AddMember ctor
        
        recordType    

    static member internal GetDataRowType (columns: Column list) = 
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)

        columns |> List.mapi( fun i col ->
            let name = col.Name
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." (i + 1)

            let propertyType = col.ClrTypeConsideringNullable
            if col.Nullable 
            then
                let property = ProvidedProperty(name, propertyType, GetterCode = QuotationsFactory.GetBody("GetNullableValueFromDataRow", col.TypeInfo.ClrType, name))
                if not col.ReadOnly
                then property.SetterCode <- QuotationsFactory.GetBody("SetNullableValueInDataRow", col.TypeInfo.ClrType, name)
                property
            else
                let property = ProvidedProperty(name, propertyType, GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>))
                if not col.ReadOnly
                then property.SetterCode <- fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1], typeof<obj>) @@>
                property
        )
        |> rowType.AddMembers

        rowType

    static member internal GetOutputTypes (outputColumns: Column list, resultType: ResultType, rank: ResultRank) =    
        if resultType = ResultType.DataReader 
        then 
            ResultTypes.SingleTypeResult typeof<SqlDataReader>
        elif outputColumns.IsEmpty
        then 
            ResultTypes.SingleTypeResult typeof<int>
        elif resultType = ResultType.DataTable 
        then
            let dataRowType = DesignTime.GetDataRowType outputColumns

            {
                ProvidedType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ DataTable>, [ dataRowType ])
                ErasedToType = typeof<DataTable<DataRow>>
                ProvidedRowType = Some dataRowType
                ErasedToRowType = typeof<Void>
                RowMapping = Expr.Value Unchecked.defaultof<RowMapping> 
            }

        else 
            let providedRowType, erasedToRowType, rowMapping = 
                if List.length outputColumns = 1
                then
                    let column0 = outputColumns.Head
                    let t = column0.ClrTypeConsideringNullable 
                    let values = Var("values", typeof<obj[]>)
                    let indexGet = Expr.Call(Expr.Var values, typeof<Array>.GetMethod("GetValue",[|typeof<int>|]), [Expr.Value 0])
                    None, t, Expr.Lambda(values,  indexGet) 

                elif resultType = ResultType.Records 
                then 
                    let r = DesignTime.GetRecordType outputColumns
                    let names = Expr.NewArray(typeof<string>, outputColumns |> List.map (fun x -> Expr.Value(x.Name))) 
                    Some r,
                    typeof<obj>,
                    <@@ fun values -> let data = (%%names, values) ||> Array.zip |> dict in DynamicRecord( data) |> box @@>
                else 
                    let tupleType = 
                        match outputColumns with
                        | [ x ] -> x.ClrTypeConsideringNullable
                        | xs -> Microsoft.FSharp.Reflection.FSharpType.MakeTupleType [| for x in xs -> x.ClrTypeConsideringNullable|]

                    let tupleTypeName = tupleType.PartialAssemblyQualifiedName
                    //None, tupleType, <@@ FSharpValue.PreComputeTupleConstructor (Type.GetType (tupleTypeName))  @@>
                    None, tupleType, <@@ fun values -> Type.GetType(tupleTypeName, throwOnError = true).GetConstructors().[0].Invoke(values) @@>
            
            let nullsToOptions = QuotationsFactory.MapArrayNullableItems(outputColumns, "MapArrayObjItemToOption") 
            let combineWithNullsToOptions = typeof<QuotationsFactory>.GetMethod("GetMapperWithNullsToOptions") 
            
            let genericOutputType, erasedToType = 
                if rank = ResultRank.Sequence 
                then 
                    Some( typedefof<_ seq>), typedefof<_ seq>.MakeGenericType([| erasedToRowType |])
                elif rank = ResultRank.SingleRow 
                then
                    Some( typedefof<_ option>), typedefof<_ option>.MakeGenericType([| erasedToRowType |])
                else //ResultRank.ScalarValue
                    None, erasedToRowType
                          
            {
                ProvidedType = 
                    if providedRowType.IsSome && genericOutputType.IsSome
                    then ProvidedTypeBuilder.MakeGenericType(genericOutputType.Value, [ providedRowType.Value ])
                    else erasedToType
                ErasedToType = erasedToType
                ProvidedRowType = providedRowType
                ErasedToRowType = erasedToRowType
                RowMapping = Expr.Call( combineWithNullsToOptions, [ nullsToOptions; rowMapping ])
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
            unbox cursor.["suggested_is_input"]
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
        |> Seq.map(fun (name, sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input) ->
            let direction = 
                if suggested_is_output
                then 
                    invalidArg name "Output parameters are not supported"
                else 
                    assert(suggested_is_input)
                    ParameterDirection.Input 
                    
            let typeInfo = findTypeInfoBySqlEngineTypeId(connection.ConnectionString, sqlEngineTypeId, userTypeId)

            { 
                Name = name
                TypeInfo = typeInfo 
                Direction = direction 
                DefaultValue = None
                Optional = allParametersOptional 
                Description = null 
            }
        )
        |> Seq.toList

    static member internal RewriteSqlStatementToEnableMoreThanOneParameterDeclaration(cmd: SqlCommand, why: SqlException) =  
        
        let getVariables tsql = 
            let parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql120Parser( true)
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
                |> Seq.map (fun (name, sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input) -> 
                    let oldName = 
                        match mapBack.TryGetValue name with 
                        | true, original -> original 
                        | false, _ -> name
                    oldName, (sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input)
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
                    let sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input = xs.[0] //|> Seq.exactlyOne
                    name, sqlEngineTypeId, userTypeId, suggested_is_output, suggested_is_input
                )
                |> Some
        else
            None
                
    static member internal GetExecuteArgs(cmdProvidedType: ProvidedTypeDefinition, sqlParameters: Parameter list, udttsPerSchema: Dictionary<_, ProvidedTypeDefinition>) = 
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
                            ProvidedParameter(parameterName, parameterType = typedefof<_ option>.MakeGenericType( p.TypeInfo.ClrType) , optionalValue = null)
                        else
                            if p.Direction = ParameterDirection.Output
                            then
                                ProvidedParameter(parameterName, parameterType = p.TypeInfo.ClrType.MakeByRefType(), isOut = true)
                            else                                 
                                ProvidedParameter(parameterName, parameterType = p.TypeInfo.ClrType, ?optionalValue = p.DefaultValue)
                    else
                        assert(p.Direction = ParameterDirection.Input)

                        let userDefinedTableTypeRow = 
                            if udttsPerSchema = null
                            then //SqlCommandProvider case
                                match cmdProvidedType.GetNestedType(p.TypeInfo.UdttName) with 
                                | null -> 
                                    let rowType = ProvidedTypeDefinition(p.TypeInfo.UdttName, Some typeof<obj>, HideObjectMethods = true)
                                    cmdProvidedType.AddMember rowType

                                    let parameters = [ 
                                        for p in p.TypeInfo.TableTypeColumns.Value -> 
                                            ProvidedParameter( p.Name, p.ClrTypeConsideringNullable, ?optionalValue = if p.Nullable then Some null else None) 
                                    ] 

                                    let ctor = ProvidedConstructor( parameters)
                                    ctor.InvokeCode <- fun args -> 
                                        let optionsToNulls = QuotationsFactory.MapArrayNullableItems(List.ofArray p.TypeInfo.TableTypeColumns.Value, "MapArrayOptionItemToObj") 
                                        <@@
                                            let values: obj[] = %%Expr.NewArray(typeof<obj>, [ for a in args -> Expr.Coerce(a, typeof<obj>) ])
                                            (%%optionsToNulls) values
                                            values
                                        @@>
                                    rowType.AddMember ctor
                            
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

