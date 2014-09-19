namespace FSharp.Data

open System
open System.Collections.Generic
open System.Data
open System.Data.SqlClient
open System.Diagnostics
open System.IO
open System.Reflection
open System.Collections.Concurrent

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection 

open Microsoft.SqlServer.Server

open Samples.FSharp.ProvidedTypes

open FSharp.Data.SqlClient

[<TypeProvider>]
type public SqlProgrammabilityProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let nameSpace = this.GetType().Namespace
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlProgrammabilityProvider", Some typeof<obj>, HideObjectMethods = true)

    let cache = ConcurrentDictionary<_, ProvidedTypeDefinition>()

    do 
        this.RegisterRuntimeAssemblyLocationAsProbingFolder( config) 

        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Records) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
            ],             
            instantiationFunction = (fun typeName args ->
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2]
                cache.GetOrAdd(key, this.CreateRootType)
            ) 
        )

        providerType.AddXmlDoc """
<summary>Typed access to SQL Server programmable objects: stored procedures, functions and user defined table types.</summary> 
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable, or SqlDataReader.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    member internal this.CreateRootType( typeName, connectionStringOrName, resultType, configFile) =

        if String.IsNullOrWhiteSpace connectionStringOrName then invalidArg "ConnectionStringOrName" "Value is empty!" 
        
        let connectionStringName, isByName = Configuration.ParseConnectionStringName connectionStringOrName

        let designTimeConnectionString = 
            if isByName 
            then Configuration.ReadConnectionStringFromConfigFileByName(connectionStringName, config.ResolutionFolder, configFile)
            else connectionStringOrName

        let conn = new SqlConnection(designTimeConnectionString)
        use closeConn = conn.UseLocally()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let databaseRootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        //UDTTs
        let spHostType = ProvidedTypeDefinition("User-Defined Table Types", baseType = Some typeof<obj>, HideObjectMethods = true)
        spHostType.AddMember <| ProvidedConstructor( [], InvokeCode = fun _ -> <@@ obj() @@>)        
        databaseRootType.AddMember spHostType

        let udtts = this.UDTTs( conn.ConnectionString)
        spHostType.AddMembers udtts

        let storedProcedures, functions = conn.GetRoutines() |> Array.partition (fun x -> x.Type = StoredProcedure)

        let spHostType = this.StoredProcedures(conn, storedProcedures, udtts, resultType, isByName, designTimeConnectionString, connectionStringOrName)
        databaseRootType.AddMember spHostType               
       
        let spHostType = this.Functions(conn, functions, udtts, resultType, isByName, designTimeConnectionString, connectionStringOrName)
        databaseRootType.AddMember spHostType               

        databaseRootType           

     member internal __.UDTTs( connStr) = [
        for t in dataTypeMappings.[connStr] do
            if t.TableType
            then 
                let rowType = ProvidedTypeDefinition(t.UdttName, Some typeof<obj[]>)
                    
                let parameters = [ 
                    for p in t.TvpColumns -> 
                        ProvidedParameter(p.Name, p.TypeInfo.ClrType, ?optionalValue = if p.IsNullable then Some null else None) 
                ] 

                let ctor = ProvidedConstructor( parameters)
                ctor.InvokeCode <- fun args -> Expr.NewArray(typeof<obj>, [ for a in args -> Expr.Coerce(a, typeof<obj>) ])
                rowType.AddMember ctor
                yield rowType
    ]

    member internal this.StoredProcedures(conn, routines, udtts, resultType, isByName, connectionStringName, connectionStringOrName) = 
        this.Routines("Stored Procedures", conn, routines, udtts, resultType, isByName, connectionStringName, connectionStringOrName)

    member internal this.Functions(conn, routines, udtts, resultType, isByName, connectionStringName, connectionStringOrName) = 
        this.Routines("Functions", conn, routines, udtts, resultType, isByName, connectionStringName, connectionStringOrName)

    member internal __.Routines(rootTypeName, conn, routines, udtts, resultType, isByName, connectionStringName, connectionStringOrName) = 

        let root = ProvidedTypeDefinition(rootTypeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        root.AddMembersDelayed <| fun() -> 
            [
                use close = conn.UseLocally()
                for routine in routines do 
                    let parameters = conn.GetParameters( routine.TwoPartName)

                    let commandText = routine.CommantText(parameters)
                    let outputColumns = 
                        if resultType <> ResultType.DataReader
                        then 
                            let isStoredProcedure = routine.Type = StoredProcedure
                            DesignTime.TryGetOutputColumns(conn, commandText, parameters, isStoredProcedure)
                        else 
                            Some []

                    if outputColumns.IsSome
                    then 
                        let rank = if routine.Type = ScalarValuedFunction then ResultRank.ScalarValue else ResultRank.Sequence
                        let output = DesignTime.GetOutputTypes(outputColumns.Value, resultType, rank)
        
                        let cmdEraseToType = typedefof<_ SqlCommand>.MakeGenericType( [| output.ErasedToRowType |])
                        let cmdProvidedType = ProvidedTypeDefinition(routine.TwoPartName, baseType = Some cmdEraseToType, HideObjectMethods = true)

                        do  //Record
                            output.ProvidedRowType |> Option.iter cmdProvidedType.AddMember

                        do  //ctors
                            let sqlParameters = Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
            
                            let ctor1 = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
                            let ctorArgsExceptConnection = [
                                Expr.Value commandText                      //sqlStatement
                                sqlParameters                               //parameters
                                Expr.Value resultType                       //resultType
                                Expr.Value (
                                    if routine.Type = ScalarValuedFunction 
                                    then ResultRank.ScalarValue 
                                    else ResultRank.Sequence)               //rank
                                output.RowMapping                           //rowMapping
                                Expr.Value(routine.Type = StoredProcedure)  //isStoredProcedure
                            ]
                            let ctorImpl = cmdEraseToType.GetConstructors() |> Seq.exactlyOne
                            ctor1.InvokeCode <- 
                                fun args -> 
                                    let connArg =
                                        <@@ 
                                            if not( String.IsNullOrEmpty(%%args.[0])) then Connection.String %%args.[0] 
                                            elif isByName then Connection.Name connectionStringName
                                            else Connection.String connectionStringOrName
                                        @@>
                                    Expr.NewObject(ctorImpl, connArg :: ctorArgsExceptConnection)
           
                            cmdProvidedType.AddMember ctor1

                            let ctor2 = ProvidedConstructor( [ ProvidedParameter("transaction", typeof<SqlTransaction>) ])

                            ctor2.InvokeCode <- 
                                fun args -> Expr.NewObject(ctorImpl, <@@ Connection.Transaction %%args.[0] @@> :: ctorArgsExceptConnection)

                            cmdProvidedType.AddMember ctor2

                        do  //AsyncExecute, Execute, and ToTraceString

                            let allParametersOptional = false
                            let executeArgs = DesignTime.GetExecuteArgs(cmdProvidedType, parameters, allParametersOptional, udtts)

                            let interfaceType = typedefof<ISqlCommand>
                            let name = "Execute" + if outputColumns.Value.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
                            DesignTime.AddGeneratedMethod(parameters, executeArgs, allParametersOptional, cmdProvidedType, cmdProvidedType.BaseType, output.ProvidedType, "Execute")
                            
                            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
                            DesignTime.AddGeneratedMethod(parameters, executeArgs, allParametersOptional, cmdProvidedType, cmdProvidedType.BaseType, asyncReturnType, "AsyncExecute")
                
                        yield cmdProvidedType
            ]

        root        
