namespace FSharp.Data

open System
open System.Data
open System.IO
open System.Data.SqlClient
open System.Reflection
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

[<TypeProvider>]
type public SqlCommandProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let mutable watcher = null : IDisposable

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlCommandProvider", Some typeof<obj>, HideObjectMethods = true)

    let cache = Dictionary()

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
                let key = typeName, String.Join(";", args)
                match cache.TryGetValue(key) with
                | false, _ ->
                    let v = this.CreateType typeName args
                    cache.[key] <- v
                    v
                | true, v -> v
            ) 
            
        )

        providerType.AddXmlDoc """
<summary>Typed representation of a T-SQL statement to execute against a SQL Server database.</summary> 
<param name='CommandText'>Transact-SQL statement to execute at the data source.</param>
<param name='ConnectionStringOrName'>String used to open a SQL Server database or the name of the connection string in the configuration file in the form of “name=&lt;connection string name&gt;”.</param>
<param name='ResultType'>A value that defines structure of result: Records, Tuples, DataTable or DataReader.</param>
<param name='SingleRow'>If set the query is expected to return a single row of the result set. See MSDN documentation for details on CommandBehavior.SingleRow.</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
<param name='AllParametersOptional'>If set all parameters become optional. NULL input values must be handled inside T-SQL.</param>
"""

        this.AddNamespace(nameSpace, [ providerType ])
    
    interface IDisposable with 
        member this.Dispose() = 
           if watcher <> null
           then try watcher.Dispose() with _ -> ()

    member internal this.CreateType typeName parameters = 
        let commandText : string = unbox parameters.[0] 
        let connectionStringOrName : string = unbox parameters.[1] 
        let resultType : ResultType = unbox parameters.[2] 
        let singleRow : bool = unbox parameters.[3] 
        let configFile : string = unbox parameters.[4] 
        let allParametersOptional : bool = unbox parameters.[5] 

        let resolutionFolder = config.ResolutionFolder

        let key = typeName, String.Join(";", parameters)
        let invalidator () =
            cache.Remove(key) |> ignore 
            this.Invalidate()
        let commandText, watcher' = Configuration.ParseTextAtDesignTime(commandText, resolutionFolder, invalidator)
        watcher' |> Option.iter (fun x -> watcher <- x)

        if connectionStringOrName.Trim() = ""
        then invalidArg "ConnectionStringOrName" "Value is empty!" 

        let name = Configuration.ParseConnectionStringName connectionStringOrName
            
        let designTimeConnectionString = 
            if name <> null 
            then Configuration.ReadConnectionStringFromConfigFileByName(name, resolutionFolder, configFile)
            else connectionStringOrName

        let providedCommandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        
        providedCommandType.AddMember <| ProvidedProperty( "ConnectionStringOrName", typeof<string>, [], IsStatic = true, GetterCode = fun _ -> <@@ connectionStringOrName @@>)

        use conn = new SqlConnection(designTimeConnectionString)
        conn.Open()
        conn.CheckVersion()
        conn.LoadDataTypesMap()

        let sqlParameters = this.ExtractSqlParameters(conn, commandText)

        let ctor = ProvidedConstructor( [ ProvidedParameter("transaction", typeof<SqlTransaction>) ])
        ctor.InvokeCode <- fun args -> 
            <@@ 
                let tran : SqlTransaction = %%args.[0]
                let this = new SqlCommand(commandText, tran.Connection, tran, CommandType = CommandType.Text) 
                let xs = %%Expr.NewArray( typeof<SqlParameter>, sqlParameters |> List.map QuotationsFactory.ToSqlParam)
                this.Parameters.AddRange xs
                this
            @@>

        providedCommandType.AddMember ctor

        let ctor = ProvidedConstructor( [ ProvidedParameter("connectionString", typeof<string>, optionalValue = "") ])
        ctor.InvokeCode <- fun args -> 
            <@@ 
                let runTimeConnectionString = 
                    if not( String.IsNullOrEmpty(%%args.[0]))
                    then %%args.[0]
                    elif name <> null then Configuration.GetConnectionStringRunTimeByName name
                    else designTimeConnectionString
                        
                let this = new SqlCommand(commandText, new SqlConnection(runTimeConnectionString), CommandType = CommandType.Text) 
                let xs = %%Expr.NewArray( typeof<SqlParameter>, sqlParameters |> List.map QuotationsFactory.ToSqlParam)
                this.Parameters.AddRange xs
                this
            @@>

        providedCommandType.AddMember ctor

        let executeArgs = this.GetExecuteArgsForSqlParameters(providedCommandType, sqlParameters, allParametersOptional) 
        let outputColumns = 
            if resultType <> ResultType.DataReader
            then this.GetOutputColumns(conn, commandText, sqlParameters)
            else []

        this.AddExecuteMethod(allParametersOptional, sqlParameters, executeArgs, outputColumns, providedCommandType, resultType, singleRow, commandText) 
        
        let getSqlCommandCopy = ProvidedMethod("AsSqlCommand", [], typeof<SqlCommand>)
        getSqlCommandCopy.InvokeCode <- fun args ->
            <@@
                let self : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                let clone = new SqlCommand(self.CommandText, new SqlConnection(self.Connection.ConnectionString), CommandType = self.CommandType)
                clone.Parameters.AddRange <| [| for p in self.Parameters -> SqlParameter(p.ParameterName, p.SqlDbType) |]
                clone
            @@>
        providedCommandType.AddMember getSqlCommandCopy          

        providedCommandType

    member internal this.GetOutputColumns(connection, commandText, sqlParameters) = 
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
                parameterType = (if allParametersOptional && parameterType.IsValueType then typedefof<_ option>.MakeGenericType( parameterType) else parameterType), 
                ?optionalValue = optionalValue
            )
    ]

    member internal this.GetExecuteNonQuery(allParametersOptional, paramInfos)  = 
        let body expr =
            <@@
                async {
                    let sqlCommand = %QuotationsFactory.GetSqlCommandWithParamValuesSet(expr, allParametersOptional, paramInfos)
                    //open connection async on .NET 4.5
                    if sqlCommand.Connection.State <> ConnectionState.Open then
                        sqlCommand.Connection.Open()
                    use disposable = sqlCommand.Connection.UseConnection()
                    return! sqlCommand.AsyncExecuteNonQuery()
                }
            @@>
        typeof<int>, body

    member internal __.AddExecuteMethod(allParametersOptional, paramInfos, executeArgs, outputColumns, providedCommandType, resultType, singleRow, commandText) = 
        let syncReturnType, executeMethodBody = 
            if resultType = ResultType.DataReader then
                let getExecuteBody(args : Expr list) = 
                    QuotationsFactory.GetDataReader(args, allParametersOptional, paramInfos, singleRow)
                typeof<SqlDataReader>, getExecuteBody
            else
                if outputColumns.IsEmpty
                then 
                    this.GetExecuteNonQuery(allParametersOptional, paramInfos)
                elif resultType = ResultType.DataTable
                then 
                    this.DataTable(providedCommandType, allParametersOptional, paramInfos, commandText, outputColumns, singleRow)
                else
                    let rowType, executeMethodBody = 
                        if List.length outputColumns = 1
                        then
                            let singleCol = outputColumns.Head
                            let column0Type = singleCol.ClrTypeConsideringNullable
                            column0Type, QuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, allParametersOptional, paramInfos, singleRow, singleCol)
                        else 
                            if resultType = ResultType.Tuples
                            then this.Tuples(allParametersOptional, paramInfos, outputColumns, singleRow)
                            else this.Records(providedCommandType, allParametersOptional, paramInfos, outputColumns, singleRow)
                    let returnType = 
                        if singleRow 
                        then ProvidedTypeBuilder.MakeGenericType(typedefof<_ option>, [ rowType ])  
                        else ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                           
                    returnType, executeMethodBody
                    
        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ syncReturnType ])
        let asyncExecute = ProvidedMethod("AsyncExecute", executeArgs, asyncReturnType)
        asyncExecute.InvokeCode <- executeMethodBody
        providedCommandType.AddMember asyncExecute

        let execute = ProvidedMethod("Execute",  executeArgs, syncReturnType)
        execute.InvokeCode <- fun args ->
            let runSync = ProvidedTypeBuilder.MakeGenericMethod(typeof<Async>.GetMethod("RunSynchronously"), [ syncReturnType ])
            //let callAsync = Expr.Call (Expr.Coerce (args.[0], providedCommandType), asyncExecute, args.Tail)
            let callAsync = executeMethodBody args
            Expr.Call(runSync, [ Expr.Coerce (callAsync, asyncReturnType); Expr.Value option<int>.None; Expr.Value option<CancellationToken>.None ])
        providedCommandType.AddMember execute 

    member internal this.Tuples(allParametersOptional, paramInfos, columns, singleRow) =
        let tupleType = match Seq.toArray columns with
                        | [| x |] -> x.ClrTypeConsideringNullable
                        | xs' -> FSharpType.MakeTupleType [| for x in xs' -> x.ClrTypeConsideringNullable|]

        let rowMapper = 
            let values = Var("values", typeof<obj[]>)
            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [ Expr.Var values; getTupleType ]), tupleType))

        tupleType, QuotationsFactory.GetBody("GetTypedSequence", tupleType, allParametersOptional, paramInfos, rowMapper, singleRow, columns)

    member internal this.Records( providedCommandType, allParametersOptional, paramInfos,  columns, singleRow) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for col in columns do
            let propertyName = col.Name
            if propertyName = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." col.Ordinal

            let property = ProvidedProperty(propertyName, propertyType = col.ClrTypeConsideringNullable)
            property.GetterCode <- fun args -> 
                <@@ 
                    let dict : IDictionary<string, obj> = %%Expr.Coerce(args.[0], typeof<IDictionary<string, obj>>)
                    dict.[propertyName] 
                @@>

            recordType.AddMember property

        providedCommandType.AddMember recordType

        let getExecuteBody (args : Expr list) = 
            let arrayToRecord = 
                <@ 
                    fun(values : obj[]) -> 
                        let names : string[] = %%Expr.NewArray(typeof<string>, columns |> List.map (fun x -> Expr.Value(x.Name))) 
                        let dict : IDictionary<_, _> = upcast ExpandoObject()
                        (names, values) ||> Array.iter2 (fun name value -> dict.Add(name, value))
                        box dict 
                @>
            QuotationsFactory.GetTypedSequence(args, allParametersOptional, paramInfos, arrayToRecord, singleRow, columns)
                         
        upcast recordType, getExecuteBody
    
    member internal this.DataTable(providedCommandType, allParametersOptional, paramInfos, commandText, outputColumns, singleRow) =
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
                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1], typeof<obj>) @@>
                    )

            rowType.AddMember property

        providedCommandType.AddMember rowType

        let body = QuotationsFactory.GetBody("GetTypedDataTable", typeof<DataRow>, allParametersOptional, paramInfos, singleRow)
        let returnType = typedefof<_ DataTable>.MakeGenericType rowType

        returnType, body

