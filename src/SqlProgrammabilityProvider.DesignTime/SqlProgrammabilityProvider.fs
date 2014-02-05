namespace FSharp.Data.Experimental

open System
open System.Data
open System.Data.SqlClient
open System.Diagnostics
open System.Reflection
open System.IO
open System.Configuration

open Microsoft.SqlServer.Server
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

        use conn = new SqlConnection(designTimeConnectionString)
        conn.Open()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let procedures = conn.GetProcedures()

        //Stored procedures
        let spHostType = ProvidedTypeDefinition("StoredProcedures", baseType = Some typeof<obj>, HideObjectMethods = true)
        databaseRootType.AddMember spHostType

        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)
        spHostType.AddMember ctor
        
        spHostType.AddMembers
            [
                for twoPartsName, isFunction, parameters in procedures do
                    if not isFunction then
                        let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
                        let ctor = ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)
                        propertyType.AddMember ctor
                    
                        let execArgs = this.GetExecuteArgsForSqlParameters(propertyType, parameters, false)

                        propertyType.AddMemberDelayed <| fun() ->
                            let execute = ProvidedMethod("AsyncExecute", execArgs, typeof<unit Async>)
                            execute.InvokeCode <- fun _ -> <@@ async.Return () @@>
                            execute

                        let property = ProvidedProperty(twoPartsName, propertyType)
                        property.GetterCode <- fun _ -> Expr.NewObject( ctor, []) 
                        
                        yield propertyType :> MemberInfo
                        yield property :> MemberInfo
            ]

        databaseRootType.AddMember <| ProvidedProperty( "StoredProcedures", spHostType, GetterCode = fun _ -> Expr.NewObject( ctor, []))
        databaseRootType

     member internal __.GetExecuteArgsForSqlParameters(providedCommandType : ProvidedTypeDefinition, sqlParameters, allParametersOptional) = [
        for p in sqlParameters do
            let parameterName = p.Name

            let optionalValue = if allParametersOptional then Some null else None

            let parameterType = 
                if not p.TypeInfo.TableType 
                then
                    p.TypeInfo.ClrType
                else
                    assert(p.Direction = ParameterDirection.Input)
                    let rowType = ProvidedTypeDefinition(p.TypeInfo.UdttName, Some typeof<SqlDataRecord>)
                    providedCommandType.AddMember rowType
                    let parameters, metaData = 
                        [
                            for p in p.TypeInfo.TvpColumns do
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

                    ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])

            yield ProvidedParameter(
                parameterName, 
                parameterType = (if allParametersOptional then typedefof<_ option>.MakeGenericType( parameterType) else parameterType), 
                ?optionalValue = optionalValue
            )
    ]


