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

open FSharp.Data.Internals
open FSharp.Data.SqlClient

open Samples.FSharp.ProvidedTypes

///<summary>Enum describing output type</summary>
type ResultType =
///<summary>Sequence of custom records with properties matching column names and types</summary>
    | Records = 0
///<summary>Sequence of tuples matching column types with the same order</summary>
    | Tuples = 1
///<summary>Typed DataTable <see cref='T:FSharp.Data.DataTable`1'/></summary>
    | DataTable = 2
///<summary>raw DataReader</summary>
    | DataReader = 3

[<assembly:TypeProviderAssembly()>]
[<assembly:InternalsVisibleTo("SqlClient.Tests")>]
do()

type internal Output = {
    ProvidedType : Type
    ErasedToType : Type
    RowType : ProvidedTypeDefinition option
    MapperFromReader : Expr
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
            ],             
            instantiationFunction = (fun typeName args ->
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4], unbox args.[5]
                cache.GetOrAdd(key, this.CreateType)
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
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    interface IDisposable with 
        member this.Dispose() = 
           if watcher <> null
           then try watcher.Dispose() with _ -> ()

    member internal this.CreateType((typeName, commandText : string, connectionStringOrName : string, resultType : ResultType, singleRow : bool, configFile : string, allParametersOptional : bool) as key) = 
        
        let resolutionFolder = config.ResolutionFolder

        let invalidator () =
            cache.TryRemove(key) |> ignore 
            this.Invalidate()
        let commandText, watcher' = Configuration.ParseTextAtDesignTime(commandText, resolutionFolder, invalidator)
        watcher' |> Option.iter (fun x -> watcher <- x)

        if connectionStringOrName.Trim() = ""
        then invalidArg "ConnectionStringOrName" "Value is empty!" 

        let connectionStringName, isByName = Configuration.ParseConnectionStringName connectionStringOrName
            
        let designTimeConnectionString = 
            if isByName
            then Configuration.ReadConnectionStringFromConfigFileByName(connectionStringName, resolutionFolder, configFile)
            else connectionStringOrName

        use conn = new SqlConnection(designTimeConnectionString)
        conn.Open()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let sqlParameters = this.ExtractSqlParameters(conn, commandText)
        
        let outputColumns = 
            if resultType <> ResultType.DataReader
            then this.GetOutputColumns(conn, commandText, sqlParameters)
            else []
        
        let { ProvidedType = providedOutputType; ErasedToType = runtimeType; RowType = typeToAdd; MapperFromReader = mapper } as output 
            = this.GetReaderMapper(outputColumns, resultType, singleRow)
        
        let runtimeCommandType = typedefof<_ SqlCommand>.MakeGenericType( [| runtimeType |])
        let providedCommandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some runtimeCommandType, HideObjectMethods = true)
        
        typeToAdd |> Option.iter providedCommandType.AddMember

        ProvidedProperty( "ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>) 
        |> providedCommandType.AddMember

        let executeArgs = this.GetExecuteArgsForSqlParameters(providedCommandType, sqlParameters, allParametersOptional) 
        let paramExpr = Expr.NewArray( typeof<SqlParameter>, sqlParameters |> List.map QuotationsFactory.ToSqlParam)

        let ctor = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
        let methodInfo = SqlCommandFactory.GetMethod("ByConnectionString", runtimeType)
        let paramTail = [Expr.Value commandText; Expr.Value CommandType.Text; paramExpr; Expr.Value singleRow; mapper]
        ctor.InvokeCode <- fun args -> 
            let getConnString = <@@ if not( String.IsNullOrEmpty(%%args.[0])) then %%args.[0] else connectionStringOrName @@>
            Expr.Call(methodInfo, getConnString::paramTail)
           
        providedCommandType.AddMember ctor

        let ctor = ProvidedConstructor( [ ProvidedParameter("transaction", typeof<SqlTransaction>) ])
        let methodInfo = SqlCommandFactory.GetMethod("ByTransaction", runtimeType)

        ctor.InvokeCode <- fun args -> Expr.Call(methodInfo, args.[0]::paramTail)

        providedCommandType.AddMember ctor

        let interfaceType = typedefof<_ ISqlCommand>.MakeGenericType([|runtimeType|])
        let name = "Execute" + if outputColumns.IsEmpty && resultType <> ResultType.DataReader then "NonQuery" else ""
            
        this.AddExecute(sqlParameters, 
                        executeArgs, 
                        allParametersOptional, 
                        providedCommandType, 
                        providedOutputType, 
                        runtimeCommandType, 
                        interfaceType.GetMethod(name), 
                        "Execute")
        
        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ providedOutputType ])
        this.AddExecute(sqlParameters, 
                        executeArgs, 
                        allParametersOptional, 
                        providedCommandType, 
                        asyncReturnType, 
                        runtimeCommandType, 
                        interfaceType.GetMethod("Async" + name), 
                        "AsyncExecute")
                
        providedCommandType

    member internal __.AddExecute(sqlParameters, executeArgs, allParametersOptional, providedCommandType, providedOutputType, erasedType, methodCall, name) =
        let mappedParamValues (exprArgs : Expr list) = 
            (exprArgs.Tail, sqlParameters)
            ||> List.map2 (fun expr info ->
                let value = 
                    if allParametersOptional
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
        let executeMethod = ProvidedMethod(name, executeArgs, providedOutputType)
        
        executeMethod.InvokeCode <- fun exprArgs ->
            let vals = mappedParamValues(exprArgs)
            let paramValues = Expr.NewArray(typeof<string*obj>, elements = vals)
            Expr.Call( Expr.Coerce(exprArgs.[0], erasedType), methodCall, [paramValues])
        providedCommandType.AddMember executeMethod
       

    member internal __.GetReaderMapper(outputColumns, resultType, singleRow) =    
        if resultType = ResultType.DataReader 
        then 
            {
                ProvidedType = typeof<SqlDataReader>
                ErasedToType = typeof<SqlDataReader>
                RowType = None
                MapperFromReader = <@@ fun (token : CancellationToken option) (sqlReader : SqlDataReader) -> sqlReader  @@>
            }
        elif outputColumns.IsEmpty
        then 
            {
                ProvidedType = typeof<int>
                ErasedToType = typeof<int>
                RowType = None
                MapperFromReader = <@@ fun (token : CancellationToken option) (sqlReader : SqlDataReader) -> 0  @@>
            }
        elif resultType = ResultType.DataTable 
        then
            let rowType = this.GetDataRowType(outputColumns)

            {
                ProvidedType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ DataTable>, [ rowType ])
                ErasedToType = typeof<DataTable<DataRow>>
                RowType = Some rowType
                MapperFromReader = <@@ fun (token : CancellationToken option) sqlReader -> SqlCommandFactory.GetDataTable(sqlReader) @@>
            }

        else 
            let rowProvidedType, rowErasedToType, rowType, rowMapper = 
                if List.length outputColumns = 1
                then
                    let column0 = outputColumns.Head
                    let t = column0.ClrTypeConsideringNullable 
                    let values = Var("values", typeof<obj[]>)
                    let indexGet = Expr.Coerce(Expr.Call(Expr.Var values, typeof<Array>.GetMethod("GetValue",[|typeof<int>|]), [Expr.Value 0]), t)
                    t, t, None, Expr.Lambda(values,  indexGet) 

                elif resultType = ResultType.Records 
                then 
                    let r = this.GetRecordType(outputColumns)
                    let names = Expr.NewArray(typeof<string>, outputColumns |> List.map (fun x -> Expr.Value(x.Name))) 
                    upcast r,
                    typeof<obj>,
                    Some r, 
                    <@@ fun(values : obj[]) -> box( SqlCommandFactory.GetRecord(values, %%names)) @@>
                else 
                    let tupleType = 
                        match outputColumns with
                        | [ x ] -> x.ClrTypeConsideringNullable
                        | xs -> FSharpType.MakeTupleType [| for x in xs -> x.ClrTypeConsideringNullable|]
                    let values = Var("values", typeof<obj[]>)
                    let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
                    let makeTuple = Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [ Expr.Var values; getTupleType ])
                    tupleType, tupleType, None, Expr.Lambda(values, Expr.Coerce(makeTuple, tupleType))
            
            let genericOutputType, resultMapperName = 
                if singleRow 
                then typedefof<_ option>, "SingeRow" 
                else typedefof<_ seq>, "GetTypedSequence"
            
            let resultMapper = SqlCommandFactory.GetMethod(resultMapperName, rowErasedToType)
            
            let columnTypes, isNullableColumn = outputColumns |> List.map (fun c -> c.TypeInfo.ClrTypeFullName, c.IsNullable) |> List.unzip
            let mapNullables = QuotationsFactory.MapArrayNullableItems(columnTypes, isNullableColumn, "MapArrayObjItemToOption") 

            {
                ProvidedType = ProvidedTypeBuilder.MakeGenericType(genericOutputType, [ rowProvidedType ])
                ErasedToType = genericOutputType.MakeGenericType([| rowErasedToType |])
                RowType = rowType
                MapperFromReader =  Expr.Call(resultMapper, [mapNullables; rowMapper])
            }
        
    member internal this.GetOutputColumns(connection, commandText, sqlParameters) : Column list = 
        try
            connection.GetFullQualityColumnInfo(commandText) 
        with :? SqlException as why ->
            try 
                connection.FallbackToSETFMONLY(commandText, CommandType.Text, sqlParameters) 
            with :? SqlException ->
                raise why
        
    member internal this.ExtractSqlParameters(connection, commandText) =  [
            use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", connection, CommandType = CommandType.StoredProcedure)
            cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
            use reader = cmd.ExecuteReader()
            while(reader.Read()) do

                let paramName = string reader.["name"]
                let sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"]

                let udtName = Convert.ToString(value = reader.["suggested_user_type_name"])
                let direction = 
                    let output = unbox reader.["suggested_is_output"]
                    let input = unbox reader.["suggested_is_input"]
                    if input && output then ParameterDirection.InputOutput
                    elif output then ParameterDirection.Output
                    else ParameterDirection.Input
                    
                let typeInfo = 
                    match findBySqlEngineTypeIdAndUdt(sqlEngineTypeId, udtName) with
                    | Some x -> x
                    | None -> failwithf "Cannot map unbound variable of sql engine type %i and UDT %s to CLR/SqlDbType type. Parameter name: %s" sqlEngineTypeId udtName paramName

                yield { 
                    Name = paramName
                    TypeInfo = typeInfo 
                    Direction = direction 
                    DefaultValue = ""
                }
    ]

    member internal __.GetExecuteArgsForSqlParameters(providedCommandType, sqlParameters, allParametersOptional) = [
        for p in sqlParameters do
            assert p.Name.StartsWith("@")
            let parameterName = p.Name.Substring 1

            let parameterType = 
                if not p.TypeInfo.TableType 
                then
                    p.TypeInfo.ClrType
                else
                    assert(p.Direction = ParameterDirection.Input)
                    let rowType = ProvidedTypeDefinition(p.TypeInfo.UdttName, Some typeof<obj[]>)
                    providedCommandType.AddMember rowType
                    let parameters = [ 
                        for p in p.TypeInfo.TvpColumns -> 
                            ProvidedParameter(p.Name, p.TypeInfo.ClrType, ?optionalValue = if p.IsNullable then Some null else None) 
                    ] 

                    let ctor = ProvidedConstructor( parameters)
                    ctor.InvokeCode <- fun args -> Expr.NewArray(typeof<obj>, [for a in args -> Expr.Coerce(a, typeof<obj>)])
                    rowType.AddMember ctor

                    ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])

            let optionalValue = if allParametersOptional then Some null else None
            yield ProvidedParameter(
                parameterName, 
                parameterType = (if allParametersOptional then typedefof<_ option>.MakeGenericType( parameterType) else parameterType), 
                ?optionalValue = optionalValue
            )
    ]

    member internal this.GetRecordType(columns) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        let properties, ctorParameters, withParameters = 
            [
                for col in columns do
                    let propertyName = col.Name
                    if propertyName = "" 
                    then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal
                    let propType = col.ClrTypeConsideringNullable

                    let property = ProvidedProperty(propertyName, propType)
                    property.GetterCode <- fun args -> <@@ (unbox<DynamicRecord> %%args.[0]).[propertyName] @@>
                    let nullalbleParameter = ProvidedParameter(propertyName, propType, optionalValue = null)
                    let ctorPropertyName = (propertyName.[0] |> string).ToLower() + propertyName.Substring(1)    
                    let optionalValue = if col.IsNullable then Some null else None
                    let ctorParameter = ProvidedParameter(ctorPropertyName, propType, ?optionalValue = optionalValue)  
                    yield property, ctorParameter, nullalbleParameter
            ] |> List.unzip3
        recordType.AddMembers properties
        let ctor = ProvidedConstructor(ctorParameters)
        ctor.InvokeCode <- fun args ->
           let pairs =  Seq.zip args properties //Because we need original names in dictionary
                        |> Seq.map (fun (arg,p) -> <@@ (%%Expr.Value(p.Name):string), %%Expr.Coerce(arg, typeof<obj>) @@>)
                        |> List.ofSeq
           <@@
                let pairs : (string*obj) [] = %%Expr.NewArray(typeof<string * obj>, pairs)
                DynamicRecord (dict pairs)
            @@> 
        recordType.AddMember ctor
        
        let withMethod = ProvidedMethod("With", withParameters, recordType)
        withMethod.InvokeCode <- fun args ->
            let pairs =  Seq.zip args.Tail properties 
                        |> Seq.map (fun (arg,p) -> <@@ (%%Expr.Value(p.Name):string), %%Expr.Coerce(arg, typeof<obj>) @@>)
                        |> List.ofSeq
            <@@
                let record : DynamicRecord = unbox %%args.Head 
                let data = Dictionary<_,_>(record.Data())
                let pairs : (string*obj) [] = %%Expr.NewArray(typeof<string * obj>, pairs)
                for key,value in pairs do
                    if value <> null then
                        data.[key] <- value
                box(DynamicRecord data)
            @@>
        recordType.AddMember withMethod
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
                    ProvidedProperty(name, propertyType = col.ClrTypeConsideringNullable,
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
