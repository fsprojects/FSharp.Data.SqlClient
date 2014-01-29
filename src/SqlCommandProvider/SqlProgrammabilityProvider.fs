namespace FSharp.Data.Experimental

//open System
open System.Reflection
//open System.Collections.Generic
//open System.Threading
//open System.Diagnostics
//open System.Dynamic

open System.Data
open System.Data.SqlClient
//open Microsoft.SqlServer.Management.Smo
//open Microsoft.SqlServer.Management.Common

open Microsoft.FSharp.Core.CompilerServices

open FSharp.Data.Experimental.Internals

open Samples.FSharp.ProvidedTypes

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
        let designTimeConnectionString = Configuration.getConnectionString( resolutionFolder, connectionStringProvided, connectionStringName, configFile)
        
        let providedDatabaseRootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        let conn = new SqlConnection(designTimeConnectionString)
//        let server = Server( ServerConnection( conn))
//        let db = server.Databases.[conn.Database]

        //Stored procedures
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        providedDatabaseRootType.AddMember spHostType
//        spHostType.AddMembers [
//            try 
//                conn.Open()
//                for sp in db.StoredProcedures do
//                    let twoPartsName = sprintf "%s.%s" sp.Schema sp.Name 
//                    yield ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
//            finally
//                conn.Close()
//        ]

        providedDatabaseRootType

[<assembly:TypeProviderAssembly()>]
do()

