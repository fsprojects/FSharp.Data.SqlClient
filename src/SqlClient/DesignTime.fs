namespace FSharp.Data.SqlClient

open System
open System.Reflection
open System.Data
open System.Data.SqlClient
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
        (sqlParameters: Parameter list, executeArgs: ProvidedParameter list, allParametersOptional, erasedType, providedOutputType, name) =

        let mappedParamValues (exprArgs: Expr list) = 
            (exprArgs.Tail, sqlParameters)
            ||> List.map2 (fun expr info ->
                let value = 
                    if allParametersOptional && not info.TypeInfo.TableType
                    then 
                        typeof<QuotationsFactory>
                            .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                            .MakeGenericMethod(info.TypeInfo.ClrType)
                            .Invoke(null, [| box expr|])
                            |> unbox
                    else
                        expr
                <@@ (%%Expr.Value(info.Name) : string), %%Expr.Coerce(value, typeof<obj>) @@>
            )

        let m = ProvidedMethod(name, executeArgs, providedOutputType)
        
        m.InvokeCode <- fun exprArgs ->
            let methodInfo = typeof<ISqlCommand>.GetMethod(name)
            let vals = mappedParamValues(exprArgs)
            let paramValues = Expr.NewArray(typeof<string*obj>, elements = vals)
            Expr.Call( Expr.Coerce(exprArgs.[0], erasedType), methodInfo, [paramValues])

        m

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
            if col.IsNullable 
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
                    None, tupleType, <@@ fun values -> Type.GetType(tupleTypeName).GetConstructors().[0].Invoke(values) @@>
            
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

    static member internal ExtractParameters(connection, commandText: string) =  [
        use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", connection, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()
        while(reader.Read()) do

            let paramName = string reader.["name"]
            let sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"]

            let userTypeId = reader |> SqlDataReader.getOption<int> "suggested_user_type_id"
            let direction = 
                if unbox reader.["suggested_is_output"]
                then 
                    invalidArg paramName "Output parameters are not supported"
                else 
                    assert(unbox reader.["suggested_is_input"])
                    ParameterDirection.Input 
                    
            let typeInfo = findTypeInfoBySqlEngineTypeId(connection.ConnectionString, sqlEngineTypeId, userTypeId)

            yield { 
                Name = paramName
                TypeInfo = typeInfo 
                Direction = direction 
                DefaultValue = None
            }
    ]

    static member internal GetExecuteArgs(cmdProvidedType: ProvidedTypeDefinition, sqlParameters: Parameter list, allParametersOptional, udtts: ProvidedTypeDefinition list) = 
        [
            for p in sqlParameters do
                assert p.Name.StartsWith("@")
                let parameterName = p.Name.Substring 1

                yield 
                    if not p.TypeInfo.TableType 
                    then
                        if allParametersOptional 
                        then 
                            ProvidedParameter(parameterName, parameterType = typedefof<_ option>.MakeGenericType( p.TypeInfo.ClrType) , optionalValue = null)
                        else
                            ProvidedParameter(parameterName, parameterType = p.TypeInfo.ClrType, ?optionalValue = p.DefaultValue)
                    else
                        assert(p.Direction = ParameterDirection.Input)

                        let userDefinedTableTypeRow = 
                            match udtts |> List.tryFind (fun x -> x.Name = p.TypeInfo.UdttName) with
                            | Some x -> x
                            | None ->
                                let rowType = ProvidedTypeDefinition(p.TypeInfo.UdttName, Some typeof<obj>, HideObjectMethods = true)
                                cmdProvidedType.AddMember rowType
                                let parameters = [ 
                                    for p in p.TypeInfo.TableTypeColumns -> 
                                        ProvidedParameter( p.Name, p.TypeInfo.ClrType, ?optionalValue = if p.IsNullable then Some null else None) 
                                ] 

                                let ctor = ProvidedConstructor( parameters)
                                ctor.InvokeCode <- fun args -> Expr.NewArray(typeof<obj>, [for a in args -> Expr.Coerce(a, typeof<obj>)])
                                rowType.AddMember ctor
                            
                                rowType

                        ProvidedParameter(
                            parameterName, 
                            parameterType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ userDefinedTableTypeRow ])
                        )

        ]

