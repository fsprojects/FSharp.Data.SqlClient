namespace FSharp.Data

open System
open System.Data
open System.IO
open System.Data.SqlClient
open System.Reflection
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Configuration
open System.Runtime.Caching

open Microsoft.SqlServer.Server

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open FSharp.Data.SqlClient

open ProviderImplementation.ProvidedTypes

[<assembly:TypeProviderAssembly()>]
[<assembly:InternalsVisibleTo("SqlClient.Tests")>]
do()

[<TypeProvider>]
type public SqlCommandProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let mutable watcher = null : IDisposable

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlCommandProvider", Some typeof<obj>, HideObjectMethods = true)

    let cache = new MemoryCache(name = this.GetType().Name)

    do 
        this.Disposing.Add <| fun _ ->
            try  
                if watcher <> null then watcher.Dispose()
                cache.Dispose()
                clearDataTypesMap()
            with _ -> ()

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("CommandText", typeof<string>) 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Records) 
                ProvidedStaticParameter("SingleRow", typeof<bool>, false)   
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("AllParametersOptional", typeof<bool>, false) 
                ProvidedStaticParameter("ResolutionFolder", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = (fun typeName args ->
                let value = lazy this.CreateRootType(typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4], unbox args.[5], unbox args.[6], unbox args.[7])
                cache.GetOrAdd(typeName, value)
            ) 
            
        )

        providerType.AddXmlDoc """
<summary>Typed representation of a T-SQL statement to execute against a SQL Server database.</summary> 
<param name='CommandText'>Transact-SQL statement to execute at the data source.</param>
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable, or SqlDataReader.</param>
<param name='SingleRow'>If set the query is expected to return a single row of the result set. See MSDN documentation for details on CommandBehavior.SingleRow.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
<param name='AllParametersOptional'>If set all parameters become optional. NULL input values must be handled inside T-SQL.</param>
<param name='ResolutionFolder'>A folder to be used to resolve relative file paths to *.sql script files at compile time. The default value is the folder that contains the project or script.</param>
<param name='DataDirectory'>The name of the data directory that replaces |DataDirectory| in connection strings. The default value is the project or script directory.</param>
"""

        this.AddNamespace(nameSpace, [ providerType ])

    member internal this.CreateRootType(typeName, sqlStatementOrFile, connectionStringOrName: string, resultType, singleRow, configFile, allParametersOptional, resolutionFolder, dataDirectory) = 

        if singleRow && not (resultType = ResultType.Records || resultType = ResultType.Tuples)
        then 
            invalidArg "singleRow" "singleRow can be set only for ResultType.Records or ResultType.Tuples."
        
        let invalidator() =
            cache.Remove(typeName) |> ignore
            this.Invalidate()
            
        let sqlStatement, watcher' = 
            let sqlScriptResolutionFolder = 
                if resolutionFolder = "" 
                then config.ResolutionFolder 
                elif Path.IsPathRooted (resolutionFolder)
                then resolutionFolder
                else Path.Combine (config.ResolutionFolder, resolutionFolder)

            Configuration.ParseTextAtDesignTime(sqlStatementOrFile, sqlScriptResolutionFolder, invalidator)

        watcher' |> Option.iter (fun x -> watcher <- x)

        if connectionStringOrName.Trim() = ""
        then invalidArg "ConnectionStringOrName" "Value is empty!" 

        let connectionStringName, isByName = Configuration.ParseConnectionStringName connectionStringOrName
            
        let designTimeConnectionString = 
            if isByName
            then Configuration.ReadConnectionStringFromConfigFileByName(connectionStringName, config.ResolutionFolder, configFile) |> fst
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

        let parameters = DesignTime.ExtractParameters(conn, sqlStatement, allParametersOptional)

        let outputColumns = 
            if resultType <> ResultType.DataReader
            then DesignTime.GetOutputColumns(conn, sqlStatement, parameters, isStoredProcedure = false)
            else []

        let rank = if singleRow then ResultRank.SingleRow else ResultRank.Sequence
        let output = DesignTime.GetOutputTypes(outputColumns, resultType, rank)
        
        let cmdProvidedType = ProvidedTypeDefinition(assembly, nameSpace, typeName, Some typeof<``ISqlCommand Implementation``>, HideObjectMethods = true)

        do  
            cmdProvidedType.AddMember(ProvidedProperty("ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>))

        do  //Record
            output.ProvidedRowType |> Option.iter cmdProvidedType.AddMember

        do  //ctors
            let sqlParameters = Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
            
            let isStoredProcedure = false
            let ctorArgsExceptConnection = [
                Expr.Value sqlStatement; 
                Expr.Value isStoredProcedure 
                sqlParameters; 
                Expr.Value resultType; 
                Expr.Value rank
                output.RowMapping; 
                Expr.Value output.ErasedToRowType.PartialAssemblyQualifiedName
            ]

            let ctorImpl = typeof<``ISqlCommand Implementation``>.GetConstructors() |> Seq.exactlyOne

            do //default ctor and create factory 
                let ctor1Params = 
                    [ 
                        ProvidedParameter("connectionString", typeof<string>, optionalValue = "") 
                        ProvidedParameter("commandTimeout", typeof<int>, optionalValue = SqlCommand.DefaultTimeout) 
                    ]

                let ctor1Body(args: _ list) = 
                    let connArg =
                        <@@ 
                            if not( String.IsNullOrEmpty(%%args.[0])) then Connection.Literal %%args.[0] 
                            elif isByName then Connection.NameInConfig connectionStringName
                            else Connection.Literal connectionStringOrName
                        @@>
                    Expr.NewObject(ctorImpl, connArg :: args.[1] :: ctorArgsExceptConnection)

                cmdProvidedType.AddMember <| ProvidedConstructor(ctor1Params, InvokeCode = ctor1Body)
                cmdProvidedType.AddMember <| ProvidedMethod("Create", ctor1Params, returnType = cmdProvidedType, IsStaticMethod = true, InvokeCode = ctor1Body)
           
            do //ctor and create factory with explicit connection/transaction support
                let ctor2Params = 
                    [ 
                        ProvidedParameter("connection", typeof<SqlConnection>)
                        ProvidedParameter("transaction", typeof<SqlTransaction>, optionalValue = null) 
                        ProvidedParameter("commandTimeout", typeof<int>, optionalValue = SqlCommand.DefaultTimeout) 
                    ]

                let ctor2Body (args: _ list) = 
                    Expr.NewObject(ctorImpl, <@@ Connection.``Connection and-or Transaction``(%%args.[0], %%args.[1]) @@> :: args.[2] :: ctorArgsExceptConnection)
                    
                cmdProvidedType.AddMember <| ProvidedConstructor(ctor2Params, InvokeCode = ctor2Body)
                cmdProvidedType.AddMember <| ProvidedMethod("Create", ctor2Params, returnType = cmdProvidedType, IsStaticMethod = true, InvokeCode = ctor2Body)

        do  //AsyncExecute, Execute, and ToTraceString

            let executeArgs = DesignTime.GetExecuteArgs(cmdProvidedType, parameters, udttsPerSchema = null)

            let addRedirectToISqlCommandMethod outputType name = 
                DesignTime.AddGeneratedMethod(parameters, executeArgs, cmdProvidedType.BaseType, outputType, name) 
                |> cmdProvidedType.AddMember

            addRedirectToISqlCommandMethod output.ProvidedType "Execute" 
                            
            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
            addRedirectToISqlCommandMethod asyncReturnType "AsyncExecute" 

            addRedirectToISqlCommandMethod typeof<string> "ToTraceString" 

        cmdProvidedType

