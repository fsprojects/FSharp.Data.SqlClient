namespace FSharp.Data.Experimental

open System
open System.Collections.Generic
open System.Data
open System.Data.SqlClient
open System.Diagnostics
open System.Dynamic
open System.IO
open System.Reflection

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection 

open Microsoft.SqlServer.Server

open Samples.FSharp.ProvidedTypes

open FSharp.Data.Experimental.Internals
open FSharp.Data.Experimental.Runtime

[<assembly:TypeProviderAssembly()>]
do()

[<TypeProvider>]
type public SqlProgrammabilityProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()


    let runtimeAssembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let nameSpace = this.GetType().Namespace

    do 
        this.RegisterRuntimeAssemblyLocationAsProbingFolder( config) 

        let providerType = ProvidedTypeDefinition(runtimeAssembly, nameSpace, "SqlProgrammability", Some typeof<obj>, HideObjectMethods = true)

        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Tuples) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])
    
    member internal this.CreateType typeName parameters = 
        let connectionStringOrName : string = unbox parameters.[0] 
        let resultType : ResultType = unbox parameters.[1] 
        let configFile : string = unbox parameters.[2] 
        let dataDirectory : string = unbox parameters.[3] 

        let resolutionFolder = config.ResolutionFolder

        let value, byName = 
            match connectionStringOrName.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
            | [| "" |] -> invalidArg "ConnectionStringOrName" "Value is empty!"
            | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> tail.Trim(), true
            | _ -> connectionStringOrName, false

        let designTimeConnectionString = 
            if byName 
            then Configuration.ReadConnectionStringFromConfigFileByName(value, resolutionFolder, configFile)
            else value

        let databaseRootType = ProvidedTypeDefinition(runtimeAssembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        let ctor = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = null) ])
        ctor.InvokeCode <- fun args -> 
            <@@
                let runTimeConnectionString = 
                    if not( String.IsNullOrEmpty(%%args.[0]))
                    then %%args.[0]
                    elif byName then Configuration.GetConnectionStringRunTimeByName(connectionStringOrName)
                    else designTimeConnectionString
                        
                do
                    if dataDirectory <> ""
                    then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)
                box(new SqlConnection( runTimeConnectionString))
            @@>

        databaseRootType.AddMember ctor

        use conn = new SqlConnection(designTimeConnectionString)
        conn.Open()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        //UDTTs
        let spHostType = ProvidedTypeDefinition("User-Defined Table Types", baseType = Some typeof<obj>, HideObjectMethods = true)
        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)        
        spHostType.AddMember ctor   
        databaseRootType.AddMember spHostType

        let udttTypes = 
            [
                for t in UDTTs() do
                    let rowType = ProvidedTypeDefinition(t.UdttName, Some typeof<SqlDataRecord>)
                    let parameters, metaData = 
                        [
                            for p in t.TvpColumns do
                                let name, dbType, maxLength = p.Name, p.TypeInfo.SqlDbTypeId, int64 p.MaxLength
                                let paramMeta = 
                                    match p.TypeInfo.IsFixedLength with 
                                    | Some true -> <@@ SqlMetaData(name, enum dbType) @@>
                                    | Some false -> <@@ SqlMetaData(name, enum dbType, maxLength) @@>
                                    | _ -> failwith "Unexpected"
                                let param = 
                                    if p.IsNullable
                                    then ProvidedParameter(p.Name, p.TypeInfo.ClrType, optionalValue = null)
                                    else ProvidedParameter(p.Name, p.TypeInfo.ClrType)
                                yield param, paramMeta
                        ] |> List.unzip

                    let ctor = ProvidedConstructor(parameters)
                    ctor.InvokeCode <- fun args -> 
                        let values = Expr.NewArray(typeof<obj>, [for a in args -> Expr.Coerce(a, typeof<obj>)])
                        <@@ 
                            let result = SqlDataRecord(metaData = %%Expr.NewArray(typeof<SqlMetaData>, metaData)) 
                            let count = result.SetValues(%%values)
                            Debug.Assert(%%Expr.Value(args.Length) = count, "Unexpected return value from SqlDataRecord.SetValues.")
                            result
                        @@>
                    rowType.AddMember ctor
                    yield rowType
            ]
        spHostType.AddMembers udttTypes
                
        let procedures = conn.GetProcedures()

        //Stored procedures
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        databaseRootType.AddMember spHostType

        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)
                
        spHostType.AddMember ctor             

        spHostType.AddMembers
            [
                for twoPartsName, isFunction, parameters in procedures do                    
                    if not isFunction then                        
                        let ctor = ProvidedConstructor([])
                        ctor.InvokeCode <- fun args -> 
                            <@@ 
                                let runTimeConnectionString = 
                                    if byName then Configuration.GetConnectionStringRunTimeByName(value)
                                    else designTimeConnectionString
                        
                                do
                                    if dataDirectory <> ""
                                    then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)

                                let this = new SqlCommand(twoPartsName, new SqlConnection(runTimeConnectionString)) 
                                this.CommandType <- CommandType.StoredProcedure
                                let xs = %%Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
                                this.Parameters.AddRange xs
                                this
                            @@>
                        let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
                        propertyType.AddMember ctor
                    
                        propertyType.AddMemberDelayed <| 
                            this.AddExecuteMethod(udttTypes, designTimeConnectionString, propertyType, false, parameters, twoPartsName, isFunction, resultType, false)

                        let property = ProvidedProperty(twoPartsName, propertyType)
                        property.GetterCode <- fun _ -> Expr.NewObject( ctor, []) 
                        
                        yield propertyType :> MemberInfo
                        yield property :> MemberInfo
            ]

        databaseRootType.AddMember <| ProvidedProperty( "Stored Procedures", spHostType, GetterCode = fun _ -> Expr.NewObject( ctor, []))
               
        databaseRootType           

     member internal __.AddExecuteMethod(udttTypes, designTimeConnectionString, propertyType, allParametersOptional, parameters, twoPartsName, isFunction, resultType, singleRow) = 
        fun() -> 
            let outputColumns = 
                if resultType <> ResultType.Maps 
                then this.GetOutputColumns(designTimeConnectionString, twoPartsName, isFunction, parameters)
                else []
        
            let execArgs = this.GetExecuteArgsForSqlParameters(udttTypes, parameters, false)

            let syncReturnType, executeMethodBody = 
                if resultType = ResultType.Maps then
                    this.Maps(allParametersOptional, parameters, singleRow)
                else
                    if outputColumns.IsEmpty
                    then 
                        this.GetExecuteNonQuery(allParametersOptional, parameters)
                    elif resultType = ResultType.DataTable
                    then 
                        this.DataTable(propertyType, allParametersOptional, parameters, twoPartsName, outputColumns, singleRow)
                    else
                        let rowType, executeMethodBody = 
                            if List.length outputColumns = 1
                            then
                                let singleCol : Column = outputColumns.Head
                                let column0Type = singleCol.ClrTypeConsideringNullable
                                column0Type, QuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, allParametersOptional, parameters, singleRow, singleCol)
                            else 
                                if resultType = ResultType.Tuples
                                then this.Tuples(allParametersOptional, parameters, outputColumns, singleRow)
                                else this.Records(propertyType, allParametersOptional, parameters, outputColumns, singleRow)
                        let returnType = if singleRow then rowType else ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                           
                        returnType, executeMethodBody

            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ syncReturnType ])
            let execute = ProvidedMethod("AsyncExecute", execArgs, asyncReturnType)
            execute.InvokeCode <- executeMethodBody
            execute        

     member internal this.GetOutputColumns(designTimeConnectionString, commandText, isFunction, sqlParameters) = 
        use connection = new SqlConnection(designTimeConnectionString)
        try
            connection.Open() 
            this.GetFullQualityColumnInfo(connection, commandText) 
        with :? SqlException as why ->
            try 
                this.FallbackToSETFMONLY(connection, commandText, isFunction, sqlParameters) 
            with :? SqlException ->
                raise why

     member internal __.GetFullQualityColumnInfo(connection, commandText) = [
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", connection, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()

        while reader.Read() do
            yield { 
                Column.Name = string reader.["name"]
                Ordinal = unbox reader.["column_ordinal"]
                TypeInfo = reader.["system_type_id"] |> unbox |> findTypeInfoBySqlEngineTypeId
                IsNullable = unbox reader.["is_nullable"]
                MaxLength = reader.["max_length"] |> unbox<int16> |> int
            }
    ] 

    member internal __.FallbackToSETFMONLY(connection, commandText, isFunction, sqlParameters) = 
        let commandType = if isFunction then CommandType.Text else CommandType.StoredProcedure
        use cmd = new SqlCommand(commandText, connection, CommandType = commandType)
        for p in sqlParameters do
            cmd.Parameters.Add(p.Name, p.TypeInfo.SqlDbType) |> ignore
        use reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
        match reader.GetSchemaTable() with
        | null -> []
        | columnSchema -> 
            [
                for row in columnSchema.Rows do
                    yield { 
                        Column.Name = unbox row.["ColumnName"]
                        Ordinal = unbox row.["ColumnOrdinal"]
                        TypeInfo =
                            let t = Enum.Parse(typeof<SqlDbType>, string row.["ProviderType"]) |> unbox
                            findTypeInfoByProviderType(unbox t, "").Value
                        IsNullable = unbox row.["AllowDBNull"]
                        MaxLength = unbox row.["ColumnSize"]
                    }
            ]
     member internal this.GetExecuteNonQuery(allParametersOptional, paramInfos)  = 
        let body expr =
            <@@
                async {
                    let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet(expr, allParametersOptional, paramInfos)
                    //open connection async on .NET 4.5
                    sqlCommand.Connection.Open()
                    use ensureConnectionClosed = sqlCommand.CloseConnectionOnly()
                    let rowsAffected = sqlCommand.AsyncExecuteNonQuery()
                    return! rowsAffected  
                }
            @@>
        typeof<int>, body

     member internal __.GetExecuteArgsForSqlParameters(udttTypes, sqlParameters, allParametersOptional) = [
        for p in sqlParameters do
            let parameterName = p.Name

            let optionalValue = if allParametersOptional then Some null else None

            let parameterType = 
                if not p.TypeInfo.TableType 
                then
                    p.TypeInfo.ClrType
                else
                    assert(p.Direction = ParameterDirection.Input)
                    let rowType = udttTypes |> List.find(fun x -> x.Name = p.TypeInfo.UdttName)
                    ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])

            yield ProvidedParameter(
                parameterName, 
                parameterType = (if allParametersOptional then typedefof<_ option>.MakeGenericType( parameterType) else parameterType), 
                ?optionalValue = optionalValue
            )
    ]

     member internal this.Tuples(allParametersOptional, paramInfos, columns, singleRow) =
        let tupleType = match Seq.toArray columns with
                        | [| x |] -> x.ClrTypeConsideringNullable
                        | xs' -> FSharpType.MakeTupleType [| for x in xs' -> x.ClrTypeConsideringNullable|]

        let rowMapper = 
            let values = Var("values", typeof<obj[]>)
            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [ Expr.Var values; getTupleType ]), tupleType))

        tupleType, QuotationsFactory.GetBody("GetTypedSequence", tupleType, allParametersOptional, paramInfos, rowMapper, singleRow, columns)

    member internal this.Records( providedCommandType, allParametersOptional, paramInfos,  columns, singleRow) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for col in columns do
            let propertyName = col.Name
            if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal

            let property = ProvidedProperty(propertyName, propertyType = col.ClrTypeConsideringNullable)
            property.GetterCode <- fun args -> 
                <@@ 
                    let dict : IDictionary<string, obj> = %%Expr.Coerce(args.[0], typeof<IDictionary<string, obj>>)
                    dict.[propertyName] 
                @@>

            recordType.AddMember property

        providedCommandType.AddMember recordType

        let getExecuteBody (args : Expr list) = 
            let arrayToRecord = 
                <@ 
                    fun(values : obj[]) -> 
                        let names : string[] = %%Expr.NewArray(typeof<string>, columns |> List.map (fun x -> Expr.Value(x.Name))) 
                        let dict : IDictionary<_, _> = upcast ExpandoObject()
                        (names, values) ||> Array.iter2 (fun name value -> dict.Add(name, value))
                        box dict 
                @>
            QuotationsFactory.GetTypedSequence(args, allParametersOptional, paramInfos, arrayToRecord, singleRow, columns)
                         
        upcast recordType, getExecuteBody
    
    member internal this.DataTable(providedCommandType, allParametersOptional, paramInfos, commandText, outputColumns, singleRow) =
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
        for col in outputColumns do
            let name = col.Name
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal

            let propertyType = col.ClrTypeConsideringNullable

            let property = 
                if col.IsNullable 
                then
                    ProvidedProperty(name, propertyType = col.ClrTypeConsideringNullable,
                        GetterCode = QuotationsFactory.GetBody("GetNullableValueFromDataRow", col.TypeInfo.ClrType, name),
                        SetterCode = QuotationsFactory.GetBody("SetNullableValueInDataRow", col.TypeInfo.ClrType, name)
                    )
                else
                    ProvidedProperty(name, propertyType, 
                        GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>),
                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1], typeof<obj>) @@>
                    )

            rowType.AddMember property

        providedCommandType.AddMember rowType

        let body = QuotationsFactory.GetBody("GetTypedDataTable", typeof<DataRow>, allParametersOptional, paramInfos, singleRow)
        let returnType = typedefof<_ DataTable>.MakeGenericType rowType

        returnType, body

    member internal this.Maps(allParametersOptional, paramInfos, singleRow) =
        let readerToMap = 
            <@
                fun(reader : SqlDataReader) -> 
                    Map.ofArray<string, obj> [| 
                        for i = 0 to reader.FieldCount - 1 do
                             if not( reader.IsDBNull(i)) then yield reader.GetName(i), reader.GetValue(i)
                    |]  
            @>

        let getExecuteBody(args : Expr list) = 
            QuotationsFactory.GetRows(args, allParametersOptional, paramInfos, readerToMap, singleRow)
            
        typeof<Map<string, obj> seq>, getExecuteBody

