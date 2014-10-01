namespace FSharp.Data

open System
open System.Data
open System.IO
open System.Data.SqlClient
open System.Reflection
open System.Collections.Concurrent
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Configuration

open Microsoft.SqlServer.Server

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open FSharp.Data.SqlClient

open Samples.FSharp.ProvidedTypes

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

    let cache = ConcurrentDictionary<_, ProvidedTypeDefinition>()

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
            ],             
            instantiationFunction = (fun typeName args ->
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4], unbox args.[5], unbox args.[6]
                cache.GetOrAdd(key, this.CreateRootType)
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
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    interface IDisposable with 
        member this.Dispose() = 
           if watcher <> null
           then try watcher.Dispose() with _ -> ()

    member internal this.CreateRootType((typeName, sqlStatementOrFile, connectionStringOrName: string, resultType, singleRow, configFile, allParametersOptional, resolutionFolder) as key) = 

        if singleRow && not (resultType = ResultType.Records || resultType = ResultType.Tuples)
        then 
            invalidArg "singleRow" "singleRow can be set only for ResultType.Records or ResultType.Tuples."
        
        let invalidator () =
            cache.TryRemove(key) |> ignore 
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
            then Configuration.ReadConnectionStringFromConfigFileByName(connectionStringName, config.ResolutionFolder, configFile)
            else connectionStringOrName

        let conn = new SqlConnection(designTimeConnectionString)
        use closeConn = conn.UseLocally()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let parameters = DesignTime.ExtractParameters(conn, sqlStatement)

        let outputColumns = 
            if resultType <> ResultType.DataReader
            then DesignTime.GetOutputColumns(conn, sqlStatement, parameters, isStoredProcedure = false)
            else []

        let rank = if singleRow then ResultRank.SingleRow else ResultRank.Sequence
        let output = DesignTime.GetOutputTypes(outputColumns, resultType, rank)
        
        let cmdEraseToType = typedefof<_ SqlCommand>.MakeGenericType( [| output.ErasedToRowType |])
        let cmdProvidedType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some cmdEraseToType, HideObjectMethods = true)

        do  
            cmdProvidedType.AddMember(ProvidedProperty("ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>))

        do  //Record
            output.ProvidedRowType |> Option.iter cmdProvidedType.AddMember

        do  //ctors
            let sqlParameters = Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
            
            let ctor1 = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
            let isStoredProcedure = false
            let ctorArgsExceptConnection = [
                Expr.Value sqlStatement; 
                Expr.Value isStoredProcedure 
                sqlParameters; 
                Expr.Value resultType; 
                Expr.Value rank
                output.RowMapping; 
            ]
            let ctorImpl = cmdEraseToType.GetConstructors() |> Seq.exactlyOne
            ctor1.InvokeCode <- 
                fun args -> 
                    let connArg =
                        <@@ 
                            if not( String.IsNullOrEmpty(%%args.[0])) then Connection.Literal %%args.[0] 
                            elif isByName then Connection.NameInConfig connectionStringName
                            else Connection.Literal connectionStringOrName
                        @@>
                    Expr.NewObject(ctorImpl, connArg :: ctorArgsExceptConnection)
           
            cmdProvidedType.AddMember ctor1

            let ctor2 = ProvidedConstructor( [ ProvidedParameter("transaction", typeof<SqlTransaction>) ])

            ctor2.InvokeCode <- 
                fun args -> Expr.NewObject(ctorImpl, <@@ Connection.Transaction %%args.[0] @@> :: ctorArgsExceptConnection)

            cmdProvidedType.AddMember ctor2

        do  //AsyncExecute, Execute, and ToTraceString

            let executeArgs = DesignTime.GetExecuteArgs(cmdProvidedType, parameters, allParametersOptional, udtts = [])

            let interfaceType = typedefof<ISqlCommand>
            let name = "Execute" + if outputColumns.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
            let addRedirectToISqlCommandMethod outputType name = 
                DesignTime.AddGeneratedMethod(parameters, executeArgs, allParametersOptional, cmdProvidedType.BaseType, outputType, name) 
                |> cmdProvidedType.AddMember

            addRedirectToISqlCommandMethod output.ProvidedType "Execute" 
                            
            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
            addRedirectToISqlCommandMethod asyncReturnType "AsyncExecute" 

            addRedirectToISqlCommandMethod typeof<string> "ToTraceString" 
                
        cmdProvidedType

