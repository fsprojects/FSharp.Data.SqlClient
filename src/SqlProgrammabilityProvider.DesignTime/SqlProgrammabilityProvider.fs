namespace FSharp.Data.Experimental

open System
open System.Data.SqlClient
open System.Reflection
open System.IO
open System.Configuration

//open Microsoft.SqlServer.Server
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open Samples.FSharp.ProvidedTypes

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
                ProvidedStaticParameter("ConnectionString", typeof<string>, "") 
                ProvidedStaticParameter("ConnectionStringName", typeof<string>, "") 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Tuples) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])

    let readConnectionStringFromConfigFileByName(name: string, resolutionFolder, fileName) = 
        let path = Path.Combine(resolutionFolder, fileName)
        if not <| File.Exists path then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
        let map = ExeConfigurationFileMap( ExeConfigFilename = path)
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name path
        | _, Lazy x -> x.ConnectionString
    
    member internal this.CreateType typeName parameters = 
        let connectionString : string = unbox parameters.[0] 
        let connectionStringName : string = unbox parameters.[1] 
        let resultType : ResultType = unbox parameters.[2] 
        let configFile : string = unbox parameters.[3] 
        let dataDirectory : string = unbox parameters.[4] 

        let resolutionFolder = config.ResolutionFolder

        let connStrOrName, byName = 
            if connectionString <> "" 
            then 
                connectionString, false
            elif connectionStringName <> ""
            then 
                connectionStringName, true
            else
                failwith """When using this provider you must specify either a connection string or a connection string name. To specify a connection string, use SqlProgrammability<"...connection string...">."""

        let designTimeConnectionString = 
            if byName 
            then readConnectionStringFromConfigFileByName(connStrOrName, resolutionFolder, configFile)
            else connStrOrName


        let conn = new SqlConnection( designTimeConnectionString)
        let server = Server( ServerConnection( conn))
        let db = server.Databases.[conn.Database]

        let databaseRootType = ProvidedTypeDefinition(runtimeAssembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        let ctor = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = null) ])
        ctor.InvokeCode <- fun args -> 
            <@@
                let runTimeConnectionString = 
                    if not( String.IsNullOrEmpty(%%args.[0]))
                    then %%args.[0]
                    elif byName then Configuration.getConnectionStringAtRunTime connStrOrName
                    else designTimeConnectionString
                        
                do
                    if dataDirectory <> ""
                    then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)
                box(new SqlConnection( runTimeConnectionString))
            @@>

        databaseRootType.AddMember ctor

        //Stored procedures
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        databaseRootType.AddMember spHostType

        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)
        spHostType.AddMember ctor

        spHostType.AddMembers
            [
                for sp in db.StoredProcedures do
                    if not sp.IsSystemObject
                    then 
                        let twoPartsName = sprintf "%s.%s" sp.Schema sp.Name 
                        let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
                        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)
                        
                        propertyType.AddMemberDelayed <| fun() ->
                            let execute = ProvidedMethod("AsyncExecute", [], typeof<unit Async>)
                            execute.InvokeCode <- fun _ -> <@@ async.Return () @@>
                            execute

                        let property = ProvidedProperty(twoPartsName, propertyType)
                        property.GetterCode <- fun _ -> Expr.NewObject( ctor, []) 
                        
                        yield propertyType :> MemberInfo
                        yield property :> MemberInfo
            ]

        databaseRootType.AddMember <| ProvidedProperty( "StoredProcedures", spHostType, GetterCode = fun _ -> Expr.NewObject( ctor, []))
        databaseRootType


