namespace FSharp.Data

open System
open System.Collections.Generic
open System.Data
open System.Data.SqlClient
open System.Diagnostics
open System.IO
open System.Reflection

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

    let cache = new ProvidedTypesCache(this)

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
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3]
                cache.GetOrAdd(key, lazy this.CreateRootType key)
            ) 
        )

        providerType.AddXmlDoc """
<summary>Typed access to SQL Server programmable objects: stored procedures, functions and user defined table types.</summary> 
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable, or SqlDataReader.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
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

        databaseRootType.AddMembersDelayed <| fun () ->
            conn.GetUserSchemas() 
            |> List.map (fun schema ->
                let schemaRoot = ProvidedTypeDefinition(schema, baseType = Some typeof<obj>, HideObjectMethods = true)
                schemaRoot.AddMembersDelayed <| fun() -> 
                    [
                        let udtts = this.UDTTs (conn.ConnectionString, schema)
                        yield! udtts

                        yield! this.Routines(conn, schema, udtts, resultType, isByName, connectionStringName, connectionStringOrName)

                        yield this.Tables(conn, schema)
                    ]
                schemaRoot            
            )

        databaseRootType           

     member internal __.UDTTs( connStr, schema) = [
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
            
                        let ctor1 = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
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
                                Expr.NewObject(ctorImpl, connArg :: ctorArgsExceptConnection)

                        yield (ctor1 :> MemberInfo)
                           
                        let ctor2 = ProvidedConstructor( [ ProvidedParameter("transaction", typeof<SqlTransaction>) ])
                        ctor2.InvokeCode <- 
                            fun args -> Expr.NewObject(ctorImpl, <@@ Connection.Transaction %%args.[0] @@> :: ctorArgsExceptConnection)

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

                let sql = sprintf "SELECT * FROM %s.%s" schema tableName
                let columns = DesignTime.GetOutputColumns(conn, sql, [], isStoredProcedure = false)
                let tvpColumnNames, tvpColumnTypes, identityColumns = [ for c in columns -> c.Name, c.TypeInfo.ClrType.FullName, c.IsIdentity ] |> List.unzip3

                let dataRowType = DesignTime.GetDataRowType(columns)

                let dataTableType = ProvidedTypeDefinition(tableName, baseType = Some( typedefof<_ DataTable>.MakeGenericType(dataRowType)))

                let ctor = ProvidedConstructor []
                ctor.InvokeCode <- fun args -> 
                    <@@ 
                        let table = new DataTable<DataRow>() 
                        for name, typeName, isIdentity in List.zip3 tvpColumnNames tvpColumnTypes identityColumns do
                            let c = new DataColumn(name, Type.GetType typeName, AutoIncrement = isIdentity)
                            table.Columns.Add c
                        table
                    @@>
                dataTableType.AddMember ctor
                
                do
                    let parameters = [ 
                        for name, typeName, isIdentity in List.zip3 tvpColumnNames tvpColumnTypes identityColumns do
                            if not isIdentity 
                            then yield ProvidedParameter(name, Type.GetType typeName)
                    ]
                    let newRowMethod = ProvidedMethod("NewRow", parameters, dataRowType)
                    newRowMethod.InvokeCode <- fun args -> 
                        <@@ 
                            let table: DataTable<DataRow> = %%args.[0]
                            let row = table.NewRow()
                            let values: obj[] = %%Expr.NewArray(typeof<obj>, [for x in args.Tail -> Expr.Coerce(x, typeof<obj>)])
                            let nonIdentityColumns = [| for c in table.Columns do if not c.AutoIncrement then yield c |]
                            for i = 0 to nonIdentityColumns.Length - 1 do
                                row.[nonIdentityColumns.[i]] <- values.[i]
                            row
                        @@>
                    dataTableType.AddMember newRowMethod

                    let addRowMethod = ProvidedMethod("AddRow", parameters, typeof<unit>)
                    addRowMethod.InvokeCode <- fun args -> 
                        <@@
                            let table: DataTable<DataRow> = %%args.[0]
                            let row: DataRow = %%Expr.Call(args.Head, newRowMethod, args.Tail)
                            table.Rows.Add row
                        @@>
                    dataTableType.AddMember addRowMethod

                dataTableType.AddMember dataRowType
                dataTableType
            )
        tables
        
