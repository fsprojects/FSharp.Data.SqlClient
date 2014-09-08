[<AutoOpen>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Text
open System.Data
open System.Collections.Generic
open System.Data.SqlClient
open Microsoft.FSharp.Reflection
open Microsoft.SqlServer.TransactSql.ScriptDom
open System.IO

let DbNull = box DBNull.Value

type SqlCommand with
    member this.AsyncExecuteReader behavior =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

let private dataTypeMappings = Dictionary<string, TypeInfo[]>()

let internal findBySqlEngineTypeIdAndUdtt(connStr, id, udttName) = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.tryFind(fun x -> x.SqlEngineTypeId = id && (not x.TableType || x.UdttName = udttName))
    
let internal findTypeInfoBySqlEngineTypeId (connStr, system_type_id, user_type_id : int option) = 
    assert (dataTypeMappings.ContainsKey connStr)

    match dataTypeMappings.[connStr] |> Array.filter(fun x -> x.SqlEngineTypeId = system_type_id ) with
    | [| x |] -> x
    | xs -> 
        match xs |> Array.filter(fun x -> user_type_id.IsNone || x.UserTypeId = user_type_id.Value) with
        | [| x |] -> x
        | _-> failwithf "error resolving systemid %A and userid %A" system_type_id user_type_id

let internal UDTTs connStr =
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.filter (fun x -> x.TableType )

let internal SqlClrTypes connStr = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] 
    
let internal findTypeInfoByProviderType( connStr, sqlDbType, udttName)  = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdttName = udttName)

let internal findTypeInfoByName( connStr, name) = 
    assert (dataTypeMappings.ContainsKey connStr)
    let dbTypes = dataTypeMappings.[connStr] 
    dbTypes|> Array.find(fun x -> x.TypeName = name || x.UdttName = name)

let splitName (twoPartsName : string) =
   let parts = twoPartsName.Split('.')
   parts.[0], parts.[1]

let parseDefaultValue (value: string, typ:Type) =
    if typ = typeof<bool> 
    then box (value.Trim() = "1")
    else Convert.ChangeType(value, typ)
     
let internal ReturnValue (conn : SqlConnection) = { 
    Name = "@ReturnValue" 
    Direction = ParameterDirection.ReturnValue
    TypeInfo = findTypeInfoByName ( conn.ConnectionString, "int") 
    DefaultValue = None }

type SqlDataReader with
    member this.toOption<'a> (key:string) =
        let v = this.[key] 
        if v = DbNull then None else Some(unbox<'a> v)
    

type DataRow with
    member this.toOption<'a> (key:string) = if this.IsNull(key) then None else Some(unbox<'a> this.[key])

let rec getParamDefaultValue (body: string) (p: ProcedureParameter) = 
    match p.Value with
    | :? Literal as x ->
        match x.LiteralType with
        | LiteralType.Default | LiteralType.Null -> Some null
        | LiteralType.Integer -> x.Value |> int |> box |> Some
        | LiteralType.Money | LiteralType.Numeric -> x.Value |> decimal |> box |> Some
        | LiteralType.Real -> x.Value |> float |> box |> Some 
        | _ -> None
    | :? UnaryExpression as expr ->
        let ssss = expr.ToString()
        None
    | _ -> None 

type SqlConnection with

 //address an issue when regular Dispose on SqlConnection needed for async computation 
 //wipes out all properties like ConnectionString in addition to closing connection to db
    member this.UseConnection() =
        if this.State = ConnectionState.Closed then 
            this.Open()
            { new IDisposable with member __.Dispose() = this.Close() }
        else { new IDisposable with member __.Dispose() = () }
    
    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member internal this.GetRoutines(routineType : string) = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand( "SELECT CONCAT(ROUTINE_SCHEMA,'.',ROUTINE_NAME) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = @routineType", this)
        cmd.Parameters.AddWithValue( "@routineType", routineType) |> ignore
        use reader = cmd.ExecuteReader()
        reader 
        |> Seq.unfold (fun x -> if x.Read() then Some( x.GetString(0), reader) else None)
        |> Seq.toArray  
            
    member internal this.GetParameters( twoPartsName) =      
        assert (this.State = ConnectionState.Open)

        let spDefinition = 
            let query = sprintf "EXEC sp_helptext '%s'" twoPartsName
            use getParameterDefaultValues = new SqlCommand( query, this)
            use reader = getParameterDefaultValues.ExecuteReader()
            reader |> Seq.unfold (fun current -> if current.Read() then Some( current.GetString (0), current) else None) |> String.concat "\n"

        let parser = TSql110Parser(true)
        let tsqlReader = new StringReader(spDefinition)
        let mutable errors : IList<ParseError> = null
        let fragment = parser.Parse(tsqlReader, &errors)

        let paramDefaults = Dictionary()

        fragment.Accept {
            new TSqlFragmentVisitor() with
                member __.Visit(node : ProcedureParameter) = 
                    base.Visit node
                    paramDefaults.[node.VariableName.Value] <- getParamDefaultValue spDefinition node
        }

        let query = sprintf "
            SELECT * 
            FROM INFORMATION_SCHEMA.PARAMETERS 
            WHERE SPECIFIC_CATALOG = '%s' AND CONCAT(SPECIFIC_SCHEMA,'.',SPECIFIC_NAME) = '%s'
            ORDER BY ORDINAL_POSITION" this.Database twoPartsName
        use getParameters = new SqlCommand( query, this)
        use reader = getParameters.ExecuteReader()
        reader |> Seq.unfold (fun record -> 
            if record.Read() 
            then 
                let name = string record.["PARAMETER_NAME"]
                let direction = 
                    match string record.["PARAMETER_MODE"] with 
                    | "IN" -> ParameterDirection.Input 
                    | "OUT" -> ParameterDirection.Output
                    | "INOUT" -> ParameterDirection.InputOutput
                    | _ -> failwithf "Parameter %s has unsupported direction %O" name record.["PARAMETER_MODE"]

                let udt = 
                    match string record.["DATA_TYPE"] with
                    | "table type" -> string record.["USER_DEFINED_TYPE_NAME"]
                    | x -> x
                
                let typeInfo = findTypeInfoByName(this.ConnectionString, udt) 

                let param = { 
                    Name = name 
                    TypeInfo = typeInfo
                    Direction = direction 
                    DefaultValue = paramDefaults.[name] 
                }
                Some ( param, record)
            else 
                None
        )
        |> Seq.toList

    member internal this.GetFullQualityColumnInfo commandText = [
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()

        while reader.Read() do
            let user_type_id = reader.toOption<int> "user_type_id"
            let system_type_id = reader.["system_type_id"] |> unbox<int>
            yield { 
                Column.Name = string reader.["name"]
                Ordinal = unbox reader.["column_ordinal"]
                TypeInfo = findTypeInfoBySqlEngineTypeId (this.ConnectionString, system_type_id, user_type_id)
                IsNullable = unbox reader.["is_nullable"]
                MaxLength = reader.["max_length"] |> unbox<int16> |> int
            }
    ] 

    member internal this.FallbackToSETFMONLY(commandText, commandType, parameters) = 
        assert (this.State = ConnectionState.Open)
        
        use cmd = new SqlCommand(commandText, this, CommandType = commandType)
        for p in parameters do
            cmd.Parameters.Add(p.Name, p.TypeInfo.SqlDbType) |> ignore
        use reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
        match reader.GetSchemaTable() with
        | null -> []
        | columnSchema -> 
            [
                for row in columnSchema.Rows do
                    yield { 
                        Column.Name = unbox row.["ColumnName"]
                        Ordinal = unbox row.["ColumnOrdinal"]
                        TypeInfo =
                            let t = Enum.Parse(typeof<SqlDbType>, string row.["ProviderType"]) |> unbox
                            findTypeInfoByProviderType(this.ConnectionString, unbox t, "").Value
                        IsNullable = unbox row.["AllowDBNull"]
                        MaxLength = unbox row.["ColumnSize"]
                    }
            ]

     member internal this.GetOutputColumns(commandText, sqlParameters) = 
        try
            this.GetFullQualityColumnInfo commandText
        with :? SqlException as why ->
            try 
                this.FallbackToSETFMONLY(commandText, CommandType.StoredProcedure, sqlParameters) 
            with :? SqlException ->
                raise why

    member internal this.GetFunctionOutputColumns( functionName : string, parameters ) =
        let commandText = parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT * FROM %s(%s)" functionName  
        this.GetOutputColumns( commandText, parameters)

    member internal this.LoadDataTypesMap() = 
        if not <| dataTypeMappings.ContainsKey this.ConnectionString
        then
            assert (this.State = ConnectionState.Open)
            let providerTypes = [| 
                for row in this.GetSchema("DataTypes").Rows do
                    let fullTypeName = string row.["TypeName"]
                    let typeName, clrType = 
                        match fullTypeName.Split(',') |> List.ofArray with
                        | [name] -> name, string row.["DataType"]
                        | name::_ -> name, fullTypeName
                        | [] -> failwith "Unaccessible"
                    yield typeName,  unbox<int> row.["ProviderDbType"], clrType, row.toOption<bool> "IsFixedLength" 
            |]

            let sqlEngineTypes = [|
                use cmd = new SqlCommand("
                    SELECT t.name, ISNULL(assembly_class, t.name), t.system_type_id, t.user_type_id, t.is_table_type
                    FROM sys.types AS t
                    LEFT JOIN sys.assembly_types ON t.user_type_id = sys.assembly_types.user_type_id ", this) 
                use reader = cmd.ExecuteReader()
                while reader.Read() do
                    yield reader.GetString(0), reader.GetString(1), reader.GetByte(2) |> int, reader.GetInt32(3), reader.GetBoolean(4)
            |]

            let typeInfos = 
                query {
                    for properName, name, system_type_id, user_type_id, is_table_type in sqlEngineTypes do
                    join (typename, providerdbtype, clrType, isFixedLength) in providerTypes on (name = typename)
                    //the next line fix the issue when ADO.NET SQL provider maps tinyint to byte despite of claiming to map it to SByte according to GetSchema("DataTypes")
                    let clrTypeFixed = if system_type_id = 48 (*tinyint*) then typeof<byte>.FullName else clrType
                    let tvpColumns = 
                        if is_table_type
                        then
                            seq {
                                use cmd = new SqlCommand("
                                    SELECT c.name, c.column_id, c.system_type_id, c.user_type_id, c.is_nullable, c.max_length
                                    FROM sys.table_types AS tt
                                    INNER JOIN sys.columns AS c ON tt.type_table_object_id = c.object_id
                                    WHERE tt.user_type_id = @user_type_id
                                    ORDER BY column_id")
                                cmd.Parameters.AddWithValue("@user_type_id", user_type_id) |> ignore
                                use closeConn = this.UseConnection()
                                cmd.Connection <- this
                                use reader = cmd.ExecuteReader()
                                while reader.Read() do 
                                    let user_type_id = reader.toOption "user_type_id"
                                    let stid = reader.["system_type_id"] |> unbox<byte> |> int
                                    yield {
                                        Column.Name = string reader.["name"]
                                        Ordinal = unbox reader.["column_id"]
                                        TypeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, stid, user_type_id)
                                        IsNullable = unbox reader.["is_nullable"]
                                        MaxLength = reader.["max_length"] |> unbox<int16> |> int
                                    }
                            } 
                            |> Seq.cache
                        else
                            Seq.empty

                    select {
                        TypeName = properName
                        SqlEngineTypeId = system_type_id
                        UserTypeId = user_type_id
                        SqlDbTypeId = providerdbtype
                        IsFixedLength = isFixedLength
                        ClrTypeFullName = clrTypeFixed
                        UdttName = if is_table_type then name else ""
                        TvpColumns = tvpColumns
                    }
                }

            dataTypeMappings.Add( this.ConnectionString, Array.ofSeq typeInfos)

    member this.ClearDataTypesMap() = 
        dataTypeMappings.Clear()
