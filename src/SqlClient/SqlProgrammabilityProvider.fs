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
<param name='ResolutionFolder'>A folder to be used to resolve relative file paths at compile time. The default value is the folder that contains the project or script.</param>
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
                            DesignTime.tryGetOutputColumns conn commandText parameters isStoredProcedure
                        else 
                            Some []

                    if outputColumns.IsSome
                    then 
                        //let singleRow = routine.Type = ScalarValuedFunction
                        let rank = if routine.Type = ScalarValuedFunction then ResultRank.ScalarValue else ResultRank.Sequence
                        let output = DesignTime.getOutputTypes outputColumns.Value resultType rank
        
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

                            let executeArgs = DesignTime.getExecuteArgs cmdProvidedType parameters false udtts

                            let interfaceType = typedefof<ISqlCommand>
                            let name = "Execute" + if outputColumns.Value.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
                            let addRedirectToISqlCommandMethod = DesignTime.addGeneratedMethod parameters executeArgs false cmdProvidedType cmdProvidedType.BaseType 
                            addRedirectToISqlCommandMethod output.ProvidedType "Execute" 
                            
                            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
                            addRedirectToISqlCommandMethod asyncReturnType "AsyncExecute" 
                
                        yield cmdProvidedType
            ]

        root        
        

//    member internal __.SPs(udttTypes, procedures, conn, resultType) =
//        typeWithConnectionString "StoredProcedures"
//            <| fun () -> 
//                [
//                for twoPartsName in procedures do                    
//                    let ctor = ProvidedConstructor([ProvidedParameter("connectionString", typeof<string>)])
//                    ctor.InvokeCode <- fun args -> <@@ new SqlCommand(twoPartsName, new SqlConnection(%%args.[0]:string), CommandType = CommandType.StoredProcedure) @@>
//                    let propertyType = ProvidedTypeDefinition(twoPartsName, baseType = Some typeof<obj>, HideObjectMethods = true)
//                    propertyType.AddMember ctor
//                    
//                    propertyType.AddMemberDelayed <| fun () ->
//                        use closeConn = conn.UseConnection()
//                        let parameters = conn.GetParameters( twoPartsName)
//
//                        let outputColumns = 
//                            let hasOutputParameters = parameters |> Seq.exists(fun p -> p.Direction <> ParameterDirection.Input)
//                            if resultType <> ResultType.DataReader && not hasOutputParameters
//                            then conn.GetOutputColumns( twoPartsName, parameters)
//                            else []
//        
//                        this.AddExecuteMethod( conn, udttTypes, propertyType, twoPartsName, resultType, false, outputColumns, parameters)
//
//                    let property = ProvidedProperty(twoPartsName, propertyType)
//                    property.GetterCode <- fun args -> Expr.NewObject( ctor, [ <@@ string %%args.[0] @@> ]) 
//                        
//                    yield propertyType :> MemberInfo
//                    yield property :> MemberInfo
//            ]
////
////     member internal __.RuntimeType(resultType, singleRow) =
////        match resultType, singleRow with
////        | ResultType.DataReader, _ -> typeof<SqlDataReader>
////        | ResultType.DataTable, _ -> typeof<DataTable<DataRow>>
////        | ResultType., _ -> typeof<DataTable<DataRow>>
////
//     member internal __.AddExecuteMethod(connStr, udttTypes, propertyType, twoPartsName, resultType, singleRow, outputColumns, parameters) = 
//        let syncReturnType, executeMethodBody = 
//            if resultType = ResultType.DataReader then
//                let getExecuteBody(args : Expr list) = 
//                    ProgrammabilityQuotationsFactory.GetDataReader(args, parameters, singleRow)
//                typeof<SqlDataReader>, getExecuteBody
//            else
//                if outputColumns.IsEmpty
//                then 
//                    this.GetExecuteNonQuery(connStr, propertyType, parameters)
//                elif resultType = ResultType.DataTable
//                then 
//                    this.DataTable(propertyType, parameters, twoPartsName, outputColumns, singleRow)
//                else
//                    let rowType, executeMethodBody = 
//                        if List.length outputColumns = 1
//                        then
//                            let singleCol : Column = outputColumns.Head
//                            let column0Type = singleCol.ClrTypeConsideringNullable
//                            column0Type, ProgrammabilityQuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, parameters, singleRow, singleCol)
//                        else 
//                            if resultType = ResultType.Tuples
//                            then this.Tuples(parameters, outputColumns, singleRow)
//                            else this.Records(propertyType, parameters, outputColumns, singleRow)
//                    let returnType = if singleRow then rowType else ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
//                           
//                    returnType, executeMethodBody
//
//        let execArgs = this.GetExecuteArgsForSqlParameters(udttTypes, parameters)
//        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ syncReturnType ])
//        let execute = ProvidedMethod("AsyncExecute", execArgs, asyncReturnType)
//        execute.InvokeCode <- executeMethodBody
//        execute        
//
//     member internal this.GetOutputRecord(conn, paramInfos) = 
//        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
//        for param in ReturnValue( conn) :: paramInfos do
//            if param.Direction <> ParameterDirection.Input then
//                let propType, getter = ProgrammabilityQuotationsFactory.GetOutParameter param
//                recordType.AddMember <| ProvidedProperty(param.Name.Substring(1), propertyType = propType, GetterCode = getter)
//        recordType
//    
//     member internal this.GetExecuteNonQuery(conn, providedCommandType, paramInfos)  = 
//        let recordType = this.GetOutputRecord(conn, paramInfos)
//        providedCommandType.AddMember recordType
//
//        let inputParamters = [for p in paramInfos do if p.Direction <> ParameterDirection.Output then yield p]
//
//        let returnName = ReturnValue( conn).Name
//        let body expr =
//            <@@
//                async {
//                    let sqlCommand = %ProgrammabilityQuotationsFactory.GetSqlCommandWithParamValuesSet(expr, inputParamters)
//                    let returnValue = sqlCommand.Parameters.Add( returnName, SqlDbType.Int) 
//                    returnValue.Direction <- ParameterDirection.ReturnValue
//                    //open connection async on .NET 4.5
//                    if sqlCommand.Connection.State <> ConnectionState.Open then
//                        sqlCommand.Connection.Open()
//                    use disposable = sqlCommand.Connection.UseConnection()
//                    let! rowsAffected = sqlCommand.AsyncExecuteNonQuery()
//                    return box sqlCommand.Parameters                     
//                }
//            @@>
//        upcast recordType, body
//           
//    member internal this.Records( providedCommandType, paramInfos,  columns, singleRow) =
//        let recordType = this.RecordType(columns)
//        providedCommandType.AddMember recordType
//
//        let names = Expr.NewArray(typeof<string>, columns |> List.map (fun x -> Expr.Value(x.Name))) 
//        let arrayToRecord = <@@ fun values -> let data = (%%names, values) ||> Array.zip |> dict in DynamicRecord( data) @@>
//
//        let getExecuteBody (args : Expr list) = 
//            ProgrammabilityQuotationsFactory.GetTypedSequence<DynamicRecord>(args, paramInfos, arrayToRecord, singleRow, columns)
//                         
//        upcast recordType, getExecuteBody
//    
//    member internal this.RecordType(columns) =
//        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
//        for col in columns do
//            let propertyName = col.Name
//            if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal
//
//            let property = ProvidedProperty(propertyName, propertyType = col.ClrTypeConsideringNullable)
//            property.GetterCode <- fun args -> <@@ (unbox<DynamicRecord> %%args.[0]).[propertyName] @@>
//
//            recordType.AddMember property
//        recordType
//
//    member internal __.GetExecuteArgsForSqlParameters(udttTypes, sqlParameters) = [
//        for p in sqlParameters do
//            if p.Direction <> ParameterDirection.Output then
//                let parameterType = 
//                    if not p.TypeInfo.TableType 
//                    then
//                        p.TypeInfo.ClrType
//                    else
//                        assert(p.Direction = ParameterDirection.Input)
//                        let rowType = udttTypes |> List.find(fun x -> x.Name = p.TypeInfo.UdttName)
//                        ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
//
//                let optionalValue = 
//                    if p.DefaultValue.IsNone
//                    then if p.Direction <> ParameterDirection.Input then Some null else None
//                    else p.DefaultValue
//
//                let parType = if p.Direction <> ParameterDirection.Input
//                              then typedefof<_ option>.MakeGenericType( parameterType) 
//                              else parameterType
//
//                yield ProvidedParameter(
//                    p.Name.Substring(1), 
//                    parameterType = parType, 
//                    ?optionalValue = optionalValue
//                )
//    ]
//
//     member internal this.Tuples(paramInfos, columns, singleRow) =
//        let tupleType = match Seq.toArray columns with
//                        | [| x |] -> x.ClrTypeConsideringNullable
//                        | xs' -> FSharpType.MakeTupleType [| for x in xs' -> x.ClrTypeConsideringNullable|]
//
//        let rowMapper = 
//            let values = Var("values", typeof<obj[]>)
//            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
//            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [ Expr.Var values; getTupleType ]), tupleType))
//
//        tupleType, ProgrammabilityQuotationsFactory.GetBody("GetTypedSequence", tupleType, paramInfos, rowMapper, singleRow, columns)
//
//    member internal this.DataTable(providedCommandType, paramInfos, commandText, outputColumns, singleRow) =
//        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
//        for col in outputColumns do
//            let name = col.Name
//            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal
//
//            let propertyType = col.ClrTypeConsideringNullable
//
//            let property = 
//                if col.IsNullable 
//                then
//                    ProvidedProperty(name, propertyType = col.ClrTypeConsideringNullable,
//                        GetterCode = QuotationsFactory.GetBody("GetNullableValueFromDataRow", col.TypeInfo.ClrType, name),
//                        SetterCode = QuotationsFactory.GetBody("SetNullableValueInDataRow", col.TypeInfo.ClrType, name)
//                    )
//                else
//                    ProvidedProperty(name, propertyType, 
//                        GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>),
//                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1], typeof<obj>) @@>
//                    )
//
//            rowType.AddMember property
//
//        providedCommandType.AddMember rowType
//
//        let body = ProgrammabilityQuotationsFactory.GetBody("GetTypedDataTable", typeof<DataRow>, paramInfos, singleRow)
//        let returnType = typedefof<_ DataTable>.MakeGenericType rowType
//
//        returnType, body
//
