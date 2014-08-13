namespace FSharp.Data

open System
open System.Data
open System.IO
open System.Data.SqlClient
open System.Reflection
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Diagnostics
open System.Dynamic
open System.Runtime.CompilerServices
open System.Configuration

open Microsoft.SqlServer.Server

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open FSharp.Data.SqlClient

open Samples.FSharp.ProvidedTypes

[<assembly:TypeProviderAssembly()>]
[<assembly:InternalsVisibleTo("SqlClient.Tests")>]
do()

type internal ResultTypes = {
    ProvidedType : Type
    ErasedToType : Type
    ProvidedRowType : ProvidedTypeDefinition option
    ErasedToRowType : Type 
    RowMapping : Expr
}   with

    static member SingleTypeResult(provided, ?erasedTo)  = { 
        ProvidedType = provided
        ErasedToType = defaultArg erasedTo provided
        ProvidedRowType = None
        ErasedToRowType = typeof<unit>
        RowMapping = Expr.Value Unchecked.defaultof<RowMapping> 
    }

[<TypeProvider>]
type public SqlCommandProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let mutable watcher = null : IDisposable

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
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
        use closeConn = conn.UseConnection()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let parameters = this.ExtractParameters(conn, sqlStatement)

        let outputColumns = 
            if resultType <> ResultType.DataReader
            then this.GetOutputColumns(conn, sqlStatement, parameters)
            else []

        let output = this.GetOutputTypes(outputColumns, resultType, singleRow)
        
        let cmdEraseToType = typedefof<_ SqlCommand>.MakeGenericType( [| output.ErasedToRowType |])
        let cmdProvidedType = 
            ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some cmdEraseToType, HideObjectMethods = true)

        do  
            cmdProvidedType.AddMember(
                ProvidedProperty("ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>))

        do  //Record
            output.ProvidedRowType |> Option.iter cmdProvidedType.AddMember

        do  //ctors
            let sqlParameters = Expr.NewArray( typeof<SqlParameter>, parameters |> List.map QuotationsFactory.ToSqlParam)
            
            let ctor1 = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
            let ctorArgsExceptConnection = [Expr.Value sqlStatement; sqlParameters; Expr.Value resultType; Expr.Value singleRow; output.RowMapping ]
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

            let executeArgs = this.GetExecuteArgs(cmdProvidedType, parameters, allParametersOptional) 

            let interfaceType = typedefof<ISqlCommand>
            let name = "Execute" + if outputColumns.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
            this.AddGeneratedMethod(parameters, 
                            executeArgs, 
                            allParametersOptional, 
                            cmdProvidedType, 
                            output.ProvidedType, 
                            cmdProvidedType.BaseType, 
                            "Execute")
                            
            let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ output.ProvidedType ])
            this.AddGeneratedMethod(parameters, 
                            executeArgs, 
                            allParametersOptional, 
                            cmdProvidedType, 
                            asyncReturnType, 
                            cmdProvidedType.BaseType, 
                            "AsyncExecute")

            this.AddGeneratedMethod(parameters, 
                            executeArgs, 
                            allParametersOptional, 
                            cmdProvidedType, 
                            typeof<string>, 
                            cmdProvidedType.BaseType, 
                            "ToTraceString")
                
        cmdProvidedType

    member internal __.AddGeneratedMethod(sqlParameters, executeArgs, allParametersOptional, cmdProvidedType, providedOutputType, erasedType, name) =

        let mappedParamValues (exprArgs : Expr list) = 
            (exprArgs.Tail, sqlParameters)
            ||> List.map2 (fun expr info ->
                let value = 
                    if allParametersOptional && not info.TypeInfo.TableType
                    then 
                        typeof<QuotationsFactory>
                            .GetMethod("OptionToObj", BindingFlags.NonPublic ||| BindingFlags.Static)
                            .MakeGenericMethod(info.TypeInfo.ClrType)
                            .Invoke(null, [| box expr|])
                            |> unbox
                    else
                        expr
                <@@ (%%Expr.Value(info.Name) : string), %%Expr.Coerce(value, typeof<obj>) @@>
            )

        let m = ProvidedMethod(name, executeArgs, providedOutputType)
        
        m.InvokeCode <- fun exprArgs ->
            let methodInfo = typeof<ISqlCommand>.GetMethod(name)
            let vals = mappedParamValues(exprArgs)
            let paramValues = Expr.NewArray(typeof<string*obj>, elements = vals)
            Expr.Call( Expr.Coerce(exprArgs.[0], erasedType), methodInfo, [paramValues])

        cmdProvidedType.AddMember m

    member internal __.GetOutputTypes(outputColumns, resultType, singleRow) =    
        if resultType = ResultType.DataReader 
        then 
            ResultTypes.SingleTypeResult typeof<SqlDataReader>
        elif outputColumns.IsEmpty
        then 
            ResultTypes.SingleTypeResult typeof<int>
        elif resultType = ResultType.DataTable 
        then
            let dataRowType = this.GetDataRowType(outputColumns)

            {
                ProvidedType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ DataTable>, [ dataRowType ])
                ErasedToType = typeof<DataTable<DataRow>>
                ProvidedRowType = Some dataRowType
                ErasedToRowType = typeof<unit>
                RowMapping = Expr.Value Unchecked.defaultof<RowMapping> 
            }

        else 
            let providedRowType, erasedToRowType, rowMapping = 
                if List.length outputColumns = 1
                then
                    let column0 = outputColumns.Head
                    let t = column0.ClrTypeConsideringNullable 
                    let values = Var("values", typeof<obj[]>)
                    let indexGet = Expr.Call(Expr.Var values, typeof<Array>.GetMethod("GetValue",[|typeof<int>|]), [Expr.Value 0])
                    None, t, Expr.Lambda(values,  indexGet) 

                elif resultType = ResultType.Records 
                then 
                    let r = this.GetRecordType(outputColumns)
                    let names = Expr.NewArray(typeof<string>, outputColumns |> List.map (fun x -> Expr.Value(x.Name))) 
                    Some r,
                    typeof<obj>,
                    <@@ fun values -> let data = (%%names, values) ||> Array.zip |> dict in DynamicRecord( data) |> box @@>
                else 
                    let tupleType = 
                        match outputColumns with
                        | [ x ] -> x.ClrTypeConsideringNullable
                        | xs -> FSharpType.MakeTupleType [| for x in xs -> x.ClrTypeConsideringNullable|]

                    let tupleTypeName = tupleType.AssemblyQualifiedName
                    None, tupleType, <@@ FSharpValue.PreComputeTupleConstructor (Type.GetType (tupleTypeName))  @@>
            
            let nullsToOptions = QuotationsFactory.MapArrayNullableItems(outputColumns, "MapArrayObjItemToOption") 
            let combineWithNullsToOptions = typeof<QuotationsFactory>.GetMethod("GetMapperWithNullsToOptions") 
            
            let genericOutputType = if singleRow then typedefof<_ option> else typedefof<_ seq>
            let erasedToType = genericOutputType.MakeGenericType([| erasedToRowType |])
                          
            {
                ProvidedType = 
                    if providedRowType.IsSome
                    then ProvidedTypeBuilder.MakeGenericType(genericOutputType, [ providedRowType.Value ])
                    else erasedToType
                ErasedToType = erasedToType
                ProvidedRowType = providedRowType
                ErasedToRowType = erasedToRowType
                RowMapping = Expr.Call( combineWithNullsToOptions, [ nullsToOptions; rowMapping ])
            }
        
    member internal this.GetOutputColumns(connection, commandText, parameters) : Column list = 
        try
            connection.GetFullQualityColumnInfo(commandText) 
        with :? SqlException as why ->
            try 
                connection.FallbackToSETFMONLY(commandText, CommandType.Text, parameters) 
            with :? SqlException ->
                raise why
        
    member internal this.ExtractParameters(connection, commandText) =  [
            use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", connection, CommandType = CommandType.StoredProcedure)
            cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
            use reader = cmd.ExecuteReader()
            while(reader.Read()) do

                let paramName = string reader.["name"]
                let sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"]

                let udttName = Convert.ToString(value = reader.["suggested_user_type_name"])
                let direction = 
                    let output = unbox reader.["suggested_is_output"]
                    let input = unbox reader.["suggested_is_input"]
                    if input && output then ParameterDirection.InputOutput
                    elif output then ParameterDirection.Output
                    else ParameterDirection.Input
                    
                let typeInfo = 
                    match findBySqlEngineTypeIdAndUdtt(connection.ConnectionString, sqlEngineTypeId, udttName) with
                    | Some x -> x
                    | None -> failwithf "Cannot map unbound variable of sql engine type %i and UDT %s to CLR/SqlDbType type. Parameter name: %s" sqlEngineTypeId udttName paramName

                yield { 
                    Name = paramName
                    TypeInfo = typeInfo 
                    Direction = direction 
                    DefaultValue = ""
                }
    ]

    member internal __.GetExecuteArgs(cmdProvidedType, sqlParameters, allParametersOptional) = 
        [
            for p in sqlParameters do
                assert p.Name.StartsWith("@")
                let parameterName = p.Name.Substring 1

                yield 
                    if not p.TypeInfo.TableType 
                    then
                        if allParametersOptional 
                        then 
                            ProvidedParameter(
                                parameterName, 
                                parameterType = typedefof<_ option>.MakeGenericType( p.TypeInfo.ClrType) , 
                                optionalValue = null 
                            )
                        else
                            ProvidedParameter(parameterName, parameterType = p.TypeInfo.ClrType)
                    else
                        assert(p.Direction = ParameterDirection.Input)
                        let rowType = ProvidedTypeDefinition(p.TypeInfo.UdttName, Some typeof<obj[]>)
                        cmdProvidedType.AddMember rowType
                        let parameters = [ 
                            for p in p.TypeInfo.TvpColumns -> 
                                ProvidedParameter( p.Name, p.TypeInfo.ClrType, ?optionalValue = if p.IsNullable then Some null else None) 
                        ] 

                        let ctor = ProvidedConstructor( parameters)
                        ctor.InvokeCode <- fun args -> Expr.NewArray(typeof<obj>, [for a in args -> Expr.Coerce(a, typeof<obj>)])
                        rowType.AddMember ctor

                        ProvidedParameter(
                            parameterName, 
                            parameterType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                        )

        ]

    member internal this.GetRecordType(columns) =
        
        columns 
            |> Seq.groupBy (fun x -> x.Name) 
            |> Seq.tryFind (fun (_, xs) -> Seq.length xs > 1)
            |> Option.iter (fun (name, _) -> failwithf "Non-unique column name %s is illegal for ResultType.Records." name)
        
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        let properties, ctorParameters = 
            [
                for col in columns do
                    
                    let propertyName = col.Name

                    if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal
                    
                    let propType = col.ClrTypeConsideringNullable

                    let property = ProvidedProperty(propertyName, propType)
                    property.GetterCode <- fun args -> <@@ (unbox<DynamicRecord> %%args.[0]).[propertyName] @@>

                    let ctorPropertyName = (propertyName.[0] |> string).ToLower() + propertyName.Substring(1)    
                    let ctorParameter = ProvidedParameter(ctorPropertyName, propType)  

                    yield property, ctorParameter
            ] |> List.unzip
        recordType.AddMembers properties
        let ctor = ProvidedConstructor(ctorParameters)
        ctor.InvokeCode <- fun args ->
           let pairs =  Seq.zip args properties //Because we need original names in dictionary
                        |> Seq.map (fun (arg,p) -> <@@ (%%Expr.Value(p.Name):string), %%Expr.Coerce(arg, typeof<obj>) @@>)
                        |> List.ofSeq
           <@@
                let pairs : (string * obj) [] = %%Expr.NewArray(typeof<string * obj>, pairs)
                DynamicRecord (dict pairs)
            @@> 
        recordType.AddMember ctor
        
        recordType    

    member internal this.GetDataRowType (outputColumns) = 
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
        for col in outputColumns do
            let name = col.Name
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal

            let propertyType = col.ClrTypeConsideringNullable

            let property = 
                if col.IsNullable 
                then
                    ProvidedProperty(name, propertyType,
                        GetterCode = QuotationsFactory.GetBody("GetNullableValueFromDataRow", col.TypeInfo.ClrType, name),
                        SetterCode = QuotationsFactory.GetBody("SetNullableValueInDataRow", col.TypeInfo.ClrType, name)
                    )
                else
                    ProvidedProperty(name, propertyType, 
                        GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>),
                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- box %%args.[1] @@>
                    )

            rowType.AddMember property
        rowType
