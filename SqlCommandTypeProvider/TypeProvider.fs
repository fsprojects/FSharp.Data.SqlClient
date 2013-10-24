namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open System.Collections.Generic
open System.Threading

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

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlCommand", Some typeof<obj>, HideObjectMethods = true)
    let invalidateE = new Event<EventHandler,EventArgs>()    

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
    
    interface ITypeProvider with
        [<CLIEvent>]
        override this.Invalidate = invalidateE.Publish

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
        let commandText = Configuration.parseTextAtDesignTime commandText resolutionFolder (fun ()-> invalidateE.Trigger(this,EventArgs()))
        let designTimeConnectionString =  Configuration.getConnectionString resolutionFolder connectionStringProvided connectionStringName configFile
        
        using(new SqlConnection(designTimeConnectionString)) <| fun conn ->
            conn.Open()
            conn.CheckVersion()
            conn.LoadDataTypesMap()
       
        let isStoredProcedure = commandType = CommandType.StoredProcedure

        let providedCommandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        providedCommandType.AddMembersDelayed <| fun () -> 
            [
                let parameters = this.ExtractParameters(designTimeConnectionString, commandText, isStoredProcedure)
                yield! this.AddPropertiesForParameters(parameters) 

                let ctor = ProvidedConstructor([ProvidedParameter("connectionString", typeof<string>, optionalValue = Unchecked.defaultof<string>)])
                ctor.InvokeCode <- fun args -> 
                    <@@ 
                        let runTimeConnectionString = 
                            if String.IsNullOrEmpty(%%args.[0])
                            then
                                Configuration.getConnectionString resolutionFolder connectionStringProvided connectionStringName configFile
                            else 
                                %%args.[0]
                        do
                            if dataDirectory <> ""
                            then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)

                        let this = new SqlCommand(commandText, new SqlConnection(runTimeConnectionString)) 
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

                yield ctor :> MemberInfo    
            ]
        
        
        let outputColumns : _ list = this.GetOutputColumns(commandText, designTimeConnectionString)
        
        this.AddExecuteMethod(outputColumns, providedCommandType, resultType, singleRow, commandText)            

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

            yield columnName, clrTypeName, unbox<int> reader.["column_ordinal"], isNullable
    ] 
    
    member __.ExtractParameters(connectionString, commandText, isStoredProcedure) : string list =  [
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

    member internal __.AddPropertiesForParameters(parameters) =  [
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

                yield prop :> MemberInfo
        ]

    member internal __.GetExecuteNonQuery() = 
        let body (args :Expr list) =
            <@@
                async {
                    let sqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>) : SqlCommand
                    //open connection async on .NET 4.5
                    sqlCommand.Connection.Open()
                    use ensureConnectionClosed = sqlCommand.CloseConnectionOnly()
                    return! sqlCommand.AsyncExecuteNonQuery() 
                }
            @@>
        typeof<int>, body

    member internal __.GetTypeForNullable(columnType, isNullable) = 
        let nakedType = Type.GetType columnType 
        if isNullable then typedefof<_ option>.MakeGenericType nakedType else nakedType

    member internal __.AddExecuteMethod(outputColumns, providedCommandType, resultType, singleRow, commandText) = 
            
        let syncReturnType, executeMethodBody = 
            if outputColumns.IsEmpty
            then 
                this.GetExecuteNonQuery()
            elif resultType = ResultType.DataTable
            then 
                this.DataTable(providedCommandType, commandText, outputColumns, singleRow)
            else
                let rowType, executeMethodBody = 
                    if outputColumns.Length = 1
                    then
                        let _, column0TypeName, _, isNullable = outputColumns.Head
                        let column0Type = this.GetTypeForNullable(column0TypeName, isNullable)
                        column0Type, QuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, singleRow, column0TypeName, isNullable)
                    elif resultType = ResultType.Tuples 
                    then 
                        this.Tuples(providedCommandType, outputColumns, singleRow)
                    else 
                        assert (resultType = ResultType.Records)
                        this.Records(providedCommandType, outputColumns, singleRow)

                //let returnType = if singleRow then rowType else ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                let returnType = 
                    match singleRow, rowType with
                    | true, _ -> rowType
                    | false, :? ProvidedTypeDefinition as providedType -> ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])     
                    | false, _ -> typedefof<_ seq>.MakeGenericType rowType     
                           
                returnType, executeMethodBody
                    
        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ syncReturnType ])
        let asyncExecute = ProvidedMethod("AsyncExecute", [], asyncReturnType, InvokeCode = executeMethodBody)
        let execute = ProvidedMethod("Execute", [], syncReturnType)
        execute.InvokeCode <- fun args ->
            let runSync = ProvidedTypeBuilder.MakeGenericMethod(typeof<Async>.GetMethod("RunSynchronously"), [ syncReturnType ])
            let callAsync = Expr.Call (Expr.Coerce (args.[0], providedCommandType), asyncExecute, [])
            Expr.Call(runSync, [ Expr.Coerce (callAsync, asyncReturnType); Expr.Value option<int>.None; Expr.Value option<CancellationToken>.None ])

        providedCommandType.AddMembers [ asyncExecute; execute ]

    member internal this.Tuples(providedCommandType, outputColumns, singleRow) =
        let columnTypes, isNullableColumn, tupleItemTypes = 
            List.unzip3 [
                for _, typeName, _, isNullable in outputColumns do
                    yield typeName, isNullable, this.GetTypeForNullable(typeName, isNullable)
            ]

        let tupleType = tupleItemTypes |> List.toArray |> FSharpType.MakeTupleType

        let rowMapper = 
            let values = Var("values", typeof<obj[]>)
            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [Expr.Var values; getTupleType]), tupleType))

        tupleType, QuotationsFactory.GetBody("GetTypedSequence", tupleType, rowMapper, singleRow, columnTypes, isNullableColumn)

    member internal this.Records(providedCommandType, outputColumns, singleRow) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for name, propertyTypeName, columnOrdinal, isNullable  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal

            let property = ProvidedProperty(name, propertyType = this.GetTypeForNullable(propertyTypeName, isNullable))
            property.GetterCode <- fun args -> 
                <@@ 
                    let values : obj[] = %%Expr.Coerce(args.[0], typeof<obj[]>)
                    values.[columnOrdinal - 1]
                @@>

            recordType.AddMember property

        providedCommandType.AddMember recordType
        let getExecuteBody (args : Expr list) = 
            let columnTypes, isNullableColumn = List.unzip [ for _, propertyTypeName, _, isNullable in outputColumns -> propertyTypeName, isNullable ] 
            QuotationsFactory.GetTypedSequence(args.[0], <@ fun(values : obj[]) -> box values @>, singleRow, columnTypes, isNullableColumn)
                         
        upcast recordType, getExecuteBody
    
    member internal this.DataTable(providedCommandType, commandText, outputColumns, singleRow) =
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
        for name, propertyTypeName, columnOrdinal, isNullable  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal

            let nakedType = Type.GetType propertyTypeName

            let property = 
                if isNullable 
                then
                    ProvidedProperty(
                        name, 
                        propertyType= typedefof<_ option>.MakeGenericType nakedType,
                        GetterCode = QuotationsFactory.GetBody("GetNullableValueFromRow", nakedType, name),
                        SetterCode = QuotationsFactory.GetBody("SetNullableValueInRow", nakedType, name)
                    )
                else
                    ProvidedProperty(
                        name, 
                        propertyType = nakedType, 
                        GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>),
                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1],  typeof<obj>) @@>
                    )

            rowType.AddMember property

        providedCommandType.AddMember rowType

        let body = QuotationsFactory.GetBody("GetTypedDataTable",  typeof<DataRow>, singleRow)
        let returnType = typedefof<_ DataTable>.MakeGenericType rowType

        returnType, body

