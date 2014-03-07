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
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Records) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
            ],             
            instantiationFunction = this.CreateType
        )

        providerType.AddXmlDoc """
<summary>Typed access to SQL Server programmable objects: stored procedures, functions and user defined table types.</summary> 
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable or Maps.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    member internal this.CreateType typeName parameters = 
        let connectionStringOrName : string = unbox parameters.[0] 
        let resultType : ResultType = unbox parameters.[1] 
        let configFile : string = unbox parameters.[2] 

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
                let input = %%args.[0] : string
                if not(String.IsNullOrEmpty(input)) then input
                elif byName then Configuration.GetConnectionStringRunTimeByName(connectionStringOrName)
                else designTimeConnectionString                        
            @@>

        databaseRootType.AddMember ctor

        use conn = new SqlConnection(designTimeConnectionString)
        conn.Open()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        //UDTTs
        let spHostType = ProvidedTypeDefinition("User-Defined Table Types", baseType = Some typeof<obj>, HideObjectMethods = true)
        spHostType.AddMember <| ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)        
        databaseRootType.AddMember spHostType

        let udttTypes = this.UDTTs()
           
        spHostType.AddMembers udttTypes
                
        //Stored procedures
        let spHostType = this.SPs(udttTypes, conn, designTimeConnectionString, resultType)
        databaseRootType.AddMember spHostType               
        databaseRootType.AddMember <| ProvidedProperty( "Stored Procedures", spHostType, GetterCode = fun args -> Expr.NewObject( ctor, [ <@@ string %%args.[0] @@>]))
               
        databaseRootType           

    member internal __.SPs(udttTypes, conn, designTimeConnectionString, resultType) =
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        let ctor = ProvidedConstructor( [ProvidedParameter("connectionString", typeof<string>)], InvokeCode = fun args -> <@@ %%args.[0] : string @@>)
        spHostType.AddMember ctor

        spHostType.AddMembers
            [
                for twoPartsName, isFunction in conn.GetProcedures() do                    
                    if not isFunction then                        
                        let ctor = ProvidedConstructor([ProvidedParameter("connectionString", typeof<string>)])
                        ctor.InvokeCode <- fun args -> <@@ new SqlCommand(twoPartsName, new SqlConnection(%%args.[0]:string), CommandType = CommandType.StoredProcedure) @@>
                        let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
                        propertyType.AddMember ctor
                    
                        propertyType.AddMemberDelayed <| 
                            this.AddExecuteMethod(udttTypes, designTimeConnectionString, propertyType, false, twoPartsName, isFunction, resultType, false)

                        let property = ProvidedProperty(twoPartsName, propertyType)
                        property.GetterCode <- fun args -> Expr.NewObject( ctor, [ <@@ string %%args.[0] @@> ]) 
                        
                        yield propertyType :> MemberInfo
                        yield property :> MemberInfo
            ]
        spHostType

     member internal __.UDTTs() =
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

     member internal __.AddExecuteMethod(udttTypes, designTimeConnectionString, propertyType, allParametersOptional, twoPartsName, isFunction, resultType, singleRow) = 
        fun() -> 
            use connection = new SqlConnection(designTimeConnectionString)
            connection.Open() 
            let parameters = connection.GetParameters(twoPartsName, isFunction)
            let outputColumns = 
                let anyOutputParameters = parameters |> Seq.exists(fun p -> p.Direction <> ParameterDirection.Input)
                if resultType <> ResultType.Maps && not anyOutputParameters
                then this.GetOutputColumns(connection, twoPartsName, isFunction, parameters)
                else []
        
            let execArgs = this.GetExecuteArgsForSqlParameters(udttTypes, parameters, false)

            let syncReturnType, executeMethodBody = 
                if resultType = ResultType.Maps then
                    this.Maps(allParametersOptional, parameters, singleRow)
                else
                    if outputColumns.IsEmpty
                    then 
                        this.GetExecuteNonQuery(propertyType, allParametersOptional, parameters)
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

     member internal this.GetOutputColumns(connection, commandText, isFunction, sqlParameters) = 
        try
            connection.GetFullQualityColumnInfo(commandText) 
        with :? SqlException as why ->
            try 
                connection.FallbackToSETFMONLY(commandText, isFunction, sqlParameters) 
            with :? SqlException ->
                raise why

     member internal this.GetOutputRecord(paramInfos) = 
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for param in ReturnValue()::paramInfos do
            if param.Direction <> ParameterDirection.Input then
                let propType, getter = QuotationsFactory.GetOutParameter param
                let property = ProvidedProperty(param.Name.Substring(1), propertyType = propType)
                property.GetterCode <- getter
                recordType.AddMember property        
        recordType
    
     member internal this.GetExecuteNonQuery(providedCommandType, allParametersOptional, paramInfos)  = 
        let recordType = this.GetOutputRecord(paramInfos)
        providedCommandType.AddMember recordType

        let inputParamters = [for p in paramInfos do if p.Direction <> ParameterDirection.Output then yield p]

        let returnName = ReturnValue().Name
        let body expr =
            <@@
                async {
                    let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet(expr, allParametersOptional, inputParamters)
                    sqlCommand.Parameters.Add(SqlParameter(returnName, SqlDbType.Int, Direction = ParameterDirection.ReturnValue)) |> ignore
                    //open connection async on .NET 4.5
                    sqlCommand.Connection.Open()
                    use ensureConnectionClosed = sqlCommand.CloseConnectionOnly()
                    let! rowsAffected = sqlCommand.AsyncExecuteNonQuery()
                    return box sqlCommand.Parameters                     
                }
            @@>
        upcast recordType, body
           
    member internal this.Records( providedCommandType, allParametersOptional, paramInfos,  columns, singleRow) =
        let recordType = this.RecordType(columns)
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
    
    member internal this.RecordType(columns) =
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
        recordType

    member internal __.GetExecuteArgsForSqlParameters(udttTypes, sqlParameters, allParametersOptional) = [
        for p in sqlParameters do
            if p.Direction <> ParameterDirection.Output then
                let parameterType = 
                    if not p.TypeInfo.TableType 
                    then
                        p.TypeInfo.ClrType
                    else
                        assert(p.Direction = ParameterDirection.Input)
                        let rowType = udttTypes |> List.find(fun x -> x.Name = p.TypeInfo.UdttName)
                        ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                
                let parType = if allParametersOptional && parameterType.IsValueType 
                              then typedefof<_ option>.MakeGenericType( parameterType) 
                              else parameterType

                let optionalValue = if String.IsNullOrWhiteSpace(p.DefaultValue) 
                                    then if allParametersOptional then Some null else None
                                    else Some (Convert.ChangeType(p.DefaultValue, parameterType))

                yield ProvidedParameter(
                    p.Name, 
                    parameterType = parType, 
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

