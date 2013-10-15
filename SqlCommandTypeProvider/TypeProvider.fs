namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Reflection

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open Samples.FSharp.ProvidedTypes

type ResultType =
    | Tuples = 0
    | Records = 1
    | DataTable = 3

[<TypeProvider>]
type public SqlCommandTypeProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let runtimeAssembly = Assembly.LoadFrom(config.RuntimeAssembly)

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlCommand", Some typeof<obj>, HideObjectMethods = true)

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("CommandText", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>, "") 
                ProvidedStaticParameter("ConnectionStringName", typeof<string>, "") 
                ProvidedStaticParameter("CommandType", typeof<CommandType>, CommandType.Text) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Tuples) 
                ProvidedStaticParameter("SingleRow", typeof<bool>, false) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])

    member internal this.CreateType typeName parameters = 
        let commandText : string = unbox parameters.[0] 
        let connectionStringProvided : string = unbox parameters.[1] 
        let connectionStringName : string = unbox parameters.[2] 
        let commandType : CommandType = unbox parameters.[3] 
        let resultType : ResultType = unbox parameters.[4] 
        let singleRow : bool = unbox parameters.[5] 
        let configFile : string = unbox parameters.[6] 
        let dataDirectory : string = unbox parameters.[7] 

        let resolutionFolder = config.ResolutionFolder
        let connectionConfig = resolutionFolder, connectionStringProvided, connectionStringName, configFile
        let connectionString =  Configuration.getConnectionString connectionConfig

        using(new SqlConnection(connectionString)) <| fun conn ->
            conn.Open()
            conn.CheckVersion()
            conn.LoadDataTypesMap()
       
        let isStoredProcedure = commandType = CommandType.StoredProcedure

        let parameters = this.ExtractParameters(connectionString, commandText, isStoredProcedure)

        let providedCommandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        ProvidedConstructor(
            parameters = [],
            InvokeCode = fun _ -> 
                <@@ 
                    let connectionString = Configuration.getConnectionString (resolutionFolder,connectionStringProvided,connectionStringName,configFile)

                    do
                        if dataDirectory <> ""
                        then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)

                    let this = new SqlCommand(commandText, new SqlConnection(connectionString)) 
                    this.CommandType <- commandType
                    for x in parameters do
                        let xs = x.Split(',') 
                        let paramName = xs.[0]
                        let sqlDbType = xs.[2] |> int |> enum
                        let direction = Enum.Parse(typeof<ParameterDirection>, xs.[3]) 
                        let p = SqlParameter(paramName, sqlDbType, Direction = unbox direction)
                        this.Parameters.Add p |> ignore
                    this
                @@>
        ) 
        |> providedCommandType.AddMember 

        this.AddPropertiesForParameters(providedCommandType, parameters)

        let outputColumns : _ list = this.GetOutputColumns(commandText, connectionString)
        if outputColumns.IsEmpty
        then 
            this.AddExecuteNonQuery(providedCommandType, connectionString)
        else
            this.AddExecuteWithResult(outputColumns, providedCommandType, resultType, singleRow, connectionConfig, commandText)            

        providedCommandType

    member this.GetOutputColumns(commandText, connectionString) = [
        use conn = new SqlConnection(connectionString)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", conn, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        conn.Open()
        use reader = cmd.ExecuteReader()
        while reader.Read() do
            let columnName = string reader.["name"]
            let sqlEngineTypeId = unbox reader.["system_type_id"]
            let detailedMessage = " Column name:" + columnName
            let isNullable : bool = unbox reader.["is_nullable"]
            let clrTypeName, _ =  mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage)
//                let typeName = mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage) |> fst
//                if unbox reader.["is_nullable"] 
//                then typedefof<_ Option>.MakeGenericType(Type.GetType typeName).AssemblyQualifiedName  
//                else typeName

            yield columnName, clrTypeName, unbox<int> reader.["column_ordinal"], isNullable
    ] 
    
    member __.ExtractParameters(connectionString, commandText, isStoredProcedure) : string list =  
        [
            use conn = new SqlConnection(connectionString)
            conn.Open()

            if isStoredProcedure
            then
                //quick solution for now. Maybe better to use conn.GetSchema("ProcedureParameters")
                use cmd = new SqlCommand(commandText, conn, CommandType = CommandType.StoredProcedure)
                SqlCommandBuilder.DeriveParameters cmd
                for p in cmd.Parameters do
                    let clrTypeName = findBySqlDbType p.SqlDbType
                    yield sprintf "%s,%s,%i,%O" p.ParameterName clrTypeName (int p.SqlDbType) p.Direction 
            else
                use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", conn, CommandType = CommandType.StoredProcedure)
                cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
                use reader = cmd.ExecuteReader()
                while(reader.Read()) do
                    let paramName = string reader.["name"]
                    let sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"]
                    let detailedMessage = " Parameter name:" + paramName
                    let clrTypeName, sqlDbTypeId = mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage)
                    let direction = 
                        let output = unbox reader.["suggested_is_output"]
                        let input = unbox reader.["suggested_is_input"]
                        if input && output then ParameterDirection.InputOutput
                        elif output then ParameterDirection.Output
                        else ParameterDirection.Input

                    yield sprintf "%s,%s,%i,%O" paramName clrTypeName sqlDbTypeId direction
        ]

    member internal __.AddPropertiesForParameters(providedCommandType, parameters) =  
        providedCommandType.AddMembersDelayed <| fun() -> 
        [
            for x in parameters do
                let paramName, clrTypeName, direction = 
                    let xs = x.Split(',') 
                    let success, direction = Enum.TryParse xs.[3]
                    assert success
                    xs.[0], xs.[1], direction

                assert (paramName.StartsWith "@")

                let propertyName = if direction = ParameterDirection.ReturnValue then "SpReturnValue" else paramName.Substring 1
                let prop = ProvidedProperty(propertyName, propertyType = Type.GetType clrTypeName)
                if direction = ParameterDirection.Output || direction = ParameterDirection.InputOutput || direction = ParameterDirection.ReturnValue
                then 
                    prop.GetterCode <- fun args -> 
                        <@@ 
                            let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                            sqlCommand.Parameters.[paramName].Value
                        @@>

                if direction = ParameterDirection.Input
                then 
                    prop.SetterCode <- fun args -> 
                        <@@ 
                            let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                            sqlCommand.Parameters.[paramName].Value <- %%Expr.Coerce(args.[1], typeof<obj>)
                        @@>

                yield prop
        ]

    member internal __.AddExecuteNonQuery(commandType, connectionString) = 
        let execute = ProvidedMethod("Execute", [], typeof<Async<int>>)
        execute.InvokeCode <- fun args ->
            <@@
                async {
                    let sqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>) : SqlCommand
                    //open connection async on .NET 4.5
                    use conn = new SqlConnection(connectionString)
                    conn.Open()
                    sqlCommand.Connection <- conn
                    return! sqlCommand.AsyncExecuteNonQuery() 
                }
            @@>
        commandType.AddMember execute

    member internal __.AddExecuteWithResult(outputColumns, providedCommandType, resultType, singleRow, connectionConfig, commandText) = 
            
        let syncReturnType, executeMethodBody = 
            if resultType = ResultType.DataTable
            then this.DataTable(providedCommandType, connectionConfig, commandText, outputColumns, singleRow)
            else
                let rowType, executeMethodBody = 
                    if outputColumns.Length = 1
                    then
                        let _, column0TypeName, _, isNullable = outputColumns.Head
                        let column0Type = Type.GetType column0TypeName
                        column0Type, QuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, singleRow)
                    elif resultType = ResultType.Tuples 
                    then 
                        this.Tuples(providedCommandType, outputColumns, singleRow)
                    else 
                        assert (resultType = ResultType.Records)
                        this.Records(providedCommandType, outputColumns, singleRow)

                let returnType = if singleRow then rowType else typedefof<_ seq>.MakeGenericType rowType
                returnType, executeMethodBody
                    
        let returnType = typedefof<_ Async>.MakeGenericType syncReturnType
        providedCommandType.AddMember <| ProvidedMethod("Execute", [], returnType, InvokeCode = executeMethodBody)

    member internal this.Tuples(providedCommandType, outputColumns, singleRow) =
        let tupleType = 
            FSharpType.MakeTupleType [|
                for _, typeName, _, isNullable in outputColumns -> Type.GetType typeName
            |]

        let rowMapper = 
            let values = Var("values", typeof<obj[]>)
            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [Expr.Var values; getTupleType]), tupleType))

        tupleType, QuotationsFactory.GetBody("GetTypedSequence", tupleType, rowMapper, singleRow)

    member internal this.Records(providedCommandType, outputColumns, singleRow) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for name, propertyTypeName, columnOrdinal, isNullable  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal

            let nakedType = Type.GetType propertyTypeName

            let property = 
                if isNullable 
                then
                    ProvidedProperty(
                        name, 
                        propertyType= typedefof<_ option>.MakeGenericType nakedType,
                        GetterCode = QuotationsFactory.GetBody("GetOption", nakedType, columnOrdinal)
                    )
                else
                    ProvidedProperty(
                        name, 
                        propertyType = nakedType,
                        GetterCode = fun args -> 
                        <@@ 
                            let values : obj[] = %%Expr.Coerce(args.[0], typeof<obj[]>)
                            values.[columnOrdinal - 1]
                        @@>
                    )

            recordType.AddMember property

        providedCommandType.AddMember recordType
        let getExecuteBody (args : Expr list) = 
            QuotationsFactory.GetTypedSequence(args.[0], <@ fun(values : obj[]) -> box values @>, singleRow)
                         
        upcast recordType, getExecuteBody
    
    member internal this.DataTable(providedCommandType, connectionConfig, commandText, outputColumns, singleRow) =
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
        for name, propertyTypeName, columnOrdinal, _  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal
            let propertyType = Type.GetType propertyTypeName
            let property = ProvidedProperty(name, propertyType) 
            property.GetterCode <- fun args -> <@@ (%%args.[0] : DataRow).[name] @@>
            property.SetterCode <- fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1],  typeof<obj>) @@>

            rowType.AddMember property

        providedCommandType.AddMember rowType

        typedefof<_ DataTable>.MakeGenericType rowType, QuotationsFactory.GetBody("GetTypedDataTable",  typeof<DataRow>, singleRow)

