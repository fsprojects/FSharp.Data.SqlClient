namespace FSharp.Data

open System
open System.Collections.Generic
open System.Data
open System.Data.SqlClient
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.Caching

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection 

open Microsoft.SqlServer.Server

open ProviderImplementation.ProvidedTypes

open FSharp.Data.SqlClient

[<TypeProvider>]
type public SqlProgrammabilityProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let nameSpace = this.GetType().Namespace
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlProgrammabilityProvider", Some typeof<obj>, HideObjectMethods = true)

    let cache = new MemoryCache(name = this.GetType().Name)

    do 
        this.Disposing.Add <| fun _ -> cache.Dispose()

    do 
        this.RegisterRuntimeAssemblyLocationAsProbingFolder( config) 

        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Records) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = (fun typeName args ->
                cache.GetOrAdd(typeName, lazy this.CreateRootType(typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3]))
            ) 
        )

        providerType.AddXmlDoc """
<summary>Typed access to SQL Server programmable objects: stored procedures, functions and user defined table types.</summary> 
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable, or SqlDataReader.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
<param name='DataDirectory'>The name of the data directory that replaces |DataDirectory| in connection strings. The default value is the project or script directory.</param>
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    member internal this.CreateRootType( typeName, connectionStringOrName, resultType, configFile, dataDirectory) =
        if String.IsNullOrWhiteSpace connectionStringOrName then invalidArg "ConnectionStringOrName" "Value is empty!" 
        
        let connectionStringName, isByName = Configuration.ParseConnectionStringName connectionStringOrName

        let designTimeConnectionString = 
            if isByName 
            then Configuration.ReadConnectionStringFromConfigFileByName(connectionStringName, config.ResolutionFolder, configFile)
            else connectionStringOrName

        let dataDirectoryFullPath = 
            if dataDirectory = "" then  config.ResolutionFolder
            elif Path.IsPathRooted dataDirectory then dataDirectory
            else Path.Combine (config.ResolutionFolder, dataDirectory)

        AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectoryFullPath)

        let conn = new SqlConnection(designTimeConnectionString)
        use closeConn = conn.UseLocally()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let databaseRootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        databaseRootType.AddMember(ProvidedProperty("ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>))

        databaseRootType.AddMembersDelayed <| fun () ->
            conn.GetUserSchemas() 
            |> List.map (fun schema ->
                let schemaRoot = ProvidedTypeDefinition(schema, baseType = Some typeof<obj>, HideObjectMethods = true)
                schemaRoot.AddMembersDelayed <| fun() -> 
                    [
                        let udtts = this.UDTTs (conn.ConnectionString, schema)
                        let udttsRoot = ProvidedTypeDefinition("User-Defined Table Types", Some typeof<obj>)
                        udttsRoot.AddMembers udtts
                        yield udttsRoot

                        yield! this.Routines(conn, schema, udtts, resultType, isByName, connectionStringName, connectionStringOrName)

                        yield this.Tables(conn, schema)
                    ]
                schemaRoot            
            )

        databaseRootType           

     member internal __.UDTTs( connStr, schema) = [
        let mappings = dataTypeMappings.[connStr] |> Array.map (fun x -> sprintf "%s.%s" x.Schema x.TypeName)
        for t in dataTypeMappings.[connStr] do
            if t.TableType && t.Schema = schema
            then 
                let rowType = ProvidedTypeDefinition(t.UdttName, Some typeof<obj>, HideObjectMethods = true)
                    
                let parameters = [ 
                    for p in t.TableTypeColumns -> 
                        ProvidedParameter(p.Name, p.TypeInfo.ClrType, ?optionalValue = if p.IsNullable then Some null else None) 
                ] 

                let ctor = ProvidedConstructor( parameters)
                ctor.InvokeCode <- fun args -> Expr.NewArray(typeof<obj>, [ for a in args -> Expr.Coerce(a, typeof<obj>) ])
                rowType.AddMember ctor
                rowType.AddXmlDoc "User-Defined Table Type"
                yield rowType
    ]

    member internal __.Routines(conn, schema, udtts, resultType, isByName, connectionStringName, connectionStringOrName) = 
        [
            use close = conn.UseLocally()
            let routines = conn.GetRoutines( schema) 
            for routine in routines do
             
                let cmdProvidedType = ProvidedTypeDefinition(routine.Name, Some typeof<RuntimeSqlCommand>, HideObjectMethods = true)
                cmdProvidedType.AddXmlDoc <| 
                    match routine with 
                    | StoredProcedure _ -> "Stored Procedure"
                    | TableValuedFunction _ -> "Table-Valued Function"
                    | ScalarValuedFunction _ -> "Scalar-Valued Function"
                
                cmdProvidedType.AddMembersDelayed <| fun() ->
                    [
                        use __ = conn.UseLocally()
                        let parameters = conn.GetParameters( routine)

                        let commandText = routine.CommantText(parameters)
                        let outputColumns = 
                            if resultType <> ResultType.DataReader
                            then 
                                DesignTime.GetOutputColumns(conn, commandText, parameters, routine.IsStoredProc)
                            else 
                                []

                        let rank = match routine with ScalarValuedFunction _ -> ResultRank.ScalarValue | _ -> ResultRank.Sequence
                        let output = DesignTime.GetOutputTypes(outputColumns, resultType, rank)
        
                        do  //Record
                            output.ProvidedRowType |> Option.iter cmdProvidedType.AddMember

                        //ctors
                        let sqlParameters = Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
            
                        let ctor1 = 
                            ProvidedConstructor [ 
                                ProvidedParameter("connectionString", typeof<string>, optionalValue = "") 
                                ProvidedParameter("commandTimeout", typeof<int>, optionalValue = defaultCommandTimeout) 
                            ]

                        let ctorArgsExceptConnection = [
                            Expr.Value commandText                      //sqlStatement
                            Expr.Value(routine.IsStoredProc)  //isStoredProcedure
                            sqlParameters                               //parameters
                            Expr.Value resultType                       //resultType
                            Expr.Value (
                                match routine with 
                                | ScalarValuedFunction _ ->  
                                    ResultRank.ScalarValue 
                                | _ -> ResultRank.Sequence)               //rank
                            output.RowMapping                           //rowMapping
                            Expr.Value output.ErasedToRowType.AssemblyQualifiedName
                        ]
                        let ctorImpl = typeof<RuntimeSqlCommand>.GetConstructors() |> Seq.exactlyOne
                        ctor1.InvokeCode <- 
                            fun args -> 
                                let connArg =
                                    <@@ 
                                        if not( String.IsNullOrEmpty(%%args.[0])) then Connection.Literal %%args.[0] 
                                        elif isByName then Connection.NameInConfig connectionStringName
                                        else Connection.Literal connectionStringOrName
                                    @@>
                                Expr.NewObject(ctorImpl, connArg :: args.[1] :: ctorArgsExceptConnection)

                        yield (ctor1 :> MemberInfo)
                           
                        let ctor2 = 
                            ProvidedConstructor [ 
                                ProvidedParameter("transaction", typeof<SqlTransaction>) 
                                ProvidedParameter("commandTimeout", typeof<int>, optionalValue = defaultCommandTimeout) 
                            ]
                        ctor2.InvokeCode <- 
                            fun args -> Expr.NewObject(ctorImpl, <@@ Connection.Transaction %%args.[0] @@> :: args.[1] :: ctorArgsExceptConnection)

                        yield upcast ctor2

                        let allParametersOptional = false
                        let executeArgs = DesignTime.GetExecuteArgs(cmdProvidedType, parameters, allParametersOptional, udtts)

                        let interfaceType = typedefof<ISqlCommand>
                        let name = "Execute" + if outputColumns.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
                        yield upcast DesignTime.AddGeneratedMethod(parameters, executeArgs, allParametersOptional, cmdProvidedType.BaseType, output.ProvidedType, "Execute") 
                            
                        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
                        yield upcast DesignTime.AddGeneratedMethod(parameters, executeArgs, allParametersOptional, cmdProvidedType.BaseType, asyncReturnType, "AsyncExecute")
                    ]

                yield cmdProvidedType
        ]

    member internal __.Tables(conn: SqlConnection, schema) = 
        let tables = ProvidedTypeDefinition("Tables", Some typeof<obj>)
        tables.AddMembersDelayed <| fun() ->
            use __ = conn.UseLocally()
            conn.GetTables(schema)
            |> List.map (fun tableName -> 

                let twoPartTableName = sprintf "[%s].[%s]" schema tableName 
                let tableDirectSql = sprintf "SELECT * FROM " + twoPartTableName
                use adapter = new SqlDataAdapter(tableDirectSql, conn)
                let dataTable = adapter.FillSchema(new DataTable(twoPartTableName), SchemaType.Source)

                let columns = dataTable.Columns

                do //read column defaults
                    let query = "
                            SELECT columns.name, is_identity, OBJECT_DEFINITION(default_object_id), XProp.Value
                            FROM sys.columns 
	                            JOIN sys.tables ON columns .object_id = tables.object_id and tables.name = @tableName
	                            JOIN sys.schemas ON tables.schema_id = schemas.schema_id and schemas.name = @schema
                                OUTER APPLY fn_listextendedproperty ('MS_Description', 'schema', @schema, 'table', @tableName, 'column', columns.name) AS XProp
                            " 
                    let cmd = new SqlCommand(query, conn)
                    cmd.Parameters.AddWithValue("@tableName", tableName) |> ignore
                    cmd.Parameters.AddWithValue("@schema", schema) |> ignore
                    use reader = cmd.ExecuteReader()
                    while reader.Read() do 
                        let c = columns.[reader.GetString(0)]

                        let values = Array.zeroCreate reader.FieldCount
                        reader.GetValues(values) |> ignore
                        //set auto-increment override
                        if not( reader.IsDBNull(1)) && not c.AutoIncrement 
                        then c.AutoIncrement <- reader.GetBoolean(1)

                        //allow nullability for non-unique columns with defaults
                        if not(c.AllowDBNull || c.Unique || reader.IsDBNull(2))
                        then 
                            c.AllowDBNull <- true
                            c.ExtendedProperties.["COLUMN_DEFAULT"] <- reader.[2]

                        if not (reader.IsDBNull(3))
                        then 
                            c.ExtendedProperties.["MS_Description"] <- reader.[3]
                        
                let serializedSchema = 
                    use writer = new StringWriter()
                    dataTable.WriteXmlSchema writer
                    writer.ToString()

                //type data row
                let dataRowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
                do 
                    for c in columns do
                        let name = c.ColumnName
                        let property = 
                            if c.AllowDBNull
                            then
                                let propertType = typedefof<_ option>.MakeGenericType c.DataType
                                let property = ProvidedProperty(c.ColumnName, propertType, GetterCode = QuotationsFactory.GetBody("GetNullableValueFromDataRow", c.DataType, name))
                                if not c.ReadOnly
                                then property.SetterCode <- QuotationsFactory.GetBody("SetNullableValueInDataRow", c.DataType, name)
                                property
                            else
                                let property = ProvidedProperty(name, c.DataType, GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>))
                                if not c.ReadOnly
                                then property.SetterCode <- fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1], typeof<obj>) @@>
                                property

                        if c.ExtendedProperties.ContainsKey "MS_Description" 
                        then property.AddXmlDoc(string c.ExtendedProperties.["MS_Description"])

                        dataRowType.AddMember property

                //type data table
                let dataTableType = ProvidedTypeDefinition(tableName, baseType = Some( typedefof<_ DataTable>.MakeGenericType(dataRowType)))
                dataTableType.AddMember dataRowType

                dataTableType.AddXmlDocDelayed <| fun() ->
                    use __ = conn.UseLocally()
                    let query = sprintf "SELECT value FROM fn_listextendedproperty ('MS_Description', 'schema', '%s', 'table', '%s', default, default)" schema tableName
                    let cmd = new SqlCommand(query, conn) 
                    cmd.ExecuteScalar() |> string

                do //ctor
                    let ctor = ProvidedConstructor []
                    ctor.InvokeCode <- fun args -> 
                        <@@ 
                            let table = new DataTable<DataRow>() 
                            use reader = new StringReader( serializedSchema)
                            table.ReadXmlSchema reader
                            for c in table.Columns do
                                if not c.AllowDBNull 
                                then c.AllowDBNull <- c.ExtendedProperties.ContainsKey "COLUMN_DEFAULT"
                            table
                        @@>
                    dataTableType.AddMember ctor
                
                do
                    let parameters, updateableColumns= 
                        [ 
                            for c in columns do 
                                if not(c.AutoIncrement || c.ReadOnly)
                                then 
                                    let parameter = 
                                        if c.AllowDBNull
                                        then ProvidedParameter(c.ColumnName, parameterType = typedefof<_ option>.MakeGenericType c.DataType, optionalValue = null)
                                        else ProvidedParameter(c.ColumnName, c.DataType)

                                    yield parameter, c
                        ] 
                        |> List.sortBy (fun (_, c) -> c.AllowDBNull) //move non-nullable params in front
                        |> List.unzip


                    let methodXmlDoc = 
                        String.concat "\n" [
                            for c in updateableColumns do
                                if c.ExtendedProperties.ContainsKey "MS_Description" 
                                then 
                                    yield sprintf "<param name='%s'>%O</param>" c.ColumnName c.ExtendedProperties.["MS_Description"]
                        ]
                        
                    let invokeCode = fun (args: _ list)-> 

                        let argsValuesConverted = 
                            (args.Tail, updateableColumns)
                            ||> List.map2 (fun valueExpr c ->
                                if c.AllowDBNull
                                then 
                                    typeof<QuotationsFactory>
                                        .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                                        .MakeGenericMethod(c.DataType)
                                        .Invoke(null, [| box valueExpr |])
                                        |> unbox
                                else
                                    valueExpr
                            )

                        <@@ 
                            let table: DataTable<DataRow> = %%args.[0]
                            let row = table.NewRow()

                            let values: obj[] = %%Expr.NewArray(typeof<obj>, [ for x in argsValuesConverted -> Expr.Coerce(x, typeof<obj>) ])
                            let namesOfUpdateableColumns: string[] = %%Expr.NewArray(typeof<string>, [ for c in updateableColumns -> Expr.Value(c.ColumnName) ])
                            let optionalParams: bool[] = %%Expr.NewArray(typeof<bool>, [ for c in updateableColumns -> Expr.Value(c.AllowDBNull) ])

                            Debug.Assert(values.Length = namesOfUpdateableColumns.Length, "values.Length = namesOfUpdateableColumns.Length")
                            Debug.Assert(values.Length = optionalParams.Length, "values.Length = optionalParams.Length")

                            for name, value, optional in Array.zip3 namesOfUpdateableColumns values optionalParams do 
                                row.[name] <- if value = null && optional then box DbNull else value
                            row
                        @@>

                    let newRowMethod = ProvidedMethod("NewRow", parameters, dataRowType, InvokeCode = invokeCode)
                    newRowMethod.AddXmlDoc methodXmlDoc
                    dataTableType.AddMember newRowMethod

                    let addRowMethod = ProvidedMethod("AddRow", parameters, typeof<unit>)
                    addRowMethod.AddXmlDoc methodXmlDoc
                    addRowMethod.InvokeCode <- fun args -> 
                        let newRow = invokeCode args
                        <@@
                            let table: DataTable<DataRow> = %%args.[0]
                            let row: DataRow = %%newRow
                            table.Rows.Add row
                        @@>
                    dataTableType.AddMember addRowMethod

                do //columns accessors
                    for c in columns do
                        let name = c.ColumnName
                        dataTableType.AddMember <| 
                            ProvidedProperty(name + "Column", typeof<DataColumn>, [], GetterCode = fun args -> <@@ (%%Expr.Coerce(args.[0], typeof<DataTable>): DataTable).Columns.[name]  @@>)

                dataTableType
            )
        tables
        
