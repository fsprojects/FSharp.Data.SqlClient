namespace FSharp.Data.Experimental

open System
//open System.Data
open System.Data.SqlClient
open System.Reflection
//open System.Collections.Generic
//open System.Diagnostics
//open System.Dynamic

//open Microsoft.SqlServer.Server
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open FSharp.Data.Experimental.Internals
open Samples.FSharp.ProvidedTypes

type ResultType =
    | Tuples = 0
    | Records = 1
    | DataTable = 2
    | Maps = 3

[<assembly:TypeProviderAssembly()>]
do()

[<TypeProvider>]
type public SqlProgrammabilityProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlProgrammability", Some typeof<obj>, HideObjectMethods = true)

    do 
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
    
    member internal this.CreateType typeName parameters = 
        let connectionStringProvided : string = unbox parameters.[0] 
        let connectionStringName : string = unbox parameters.[1] 
        let resultType : ResultType = unbox parameters.[2] 
        let configFile : string = unbox parameters.[3] 
        let dataDirectory : string = unbox parameters.[4] 

        let resolutionFolder = config.ResolutionFolder
        let designTimeConnectionString = Configuration.GetConnectionString( resolutionFolder, connectionStringProvided, connectionStringName, configFile)
        
        let databaseRootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        let conn = new SqlConnection(connectionStringProvided)
        let server = Server( ServerConnection( conn))
        let db = server.Databases.[conn.Database]

        //Stored procedures
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        databaseRootType.AddMember spHostType
        spHostType.AddMembersDelayed <| fun() ->
            [
                for sp in db.StoredProcedures do
                    if not sp.IsSystemObject
                    then 
                        let twoPartsName = sprintf "%s.%s" sp.Schema sp.Name 
                        let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
                        
                        let ctor = ProvidedConstructor([], InvokeCode = fun _ -> <@@ obj() @@>) 
                        propertyType.AddMember ctor

                        propertyType.AddMemberDelayed <| fun() ->
                            let execute = ProvidedMethod("AsyncExecute", [], typeof<unit Async>)
                            execute.InvokeCode <- fun _ -> <@@ async.Return () @@>
                            execute

                        let property = ProvidedProperty(twoPartsName, propertyType)
                        property.GetterCode <- fun _ -> <@@ %%Expr.NewObject(%%Expr.Value(ctor), []) @@>
                        
                        yield propertyType :> MemberInfo
                        yield property :> MemberInfo
            ]

        databaseRootType


