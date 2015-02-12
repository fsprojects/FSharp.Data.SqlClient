[<AutoOpen>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Text
open System.Data
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open System.Data.SqlClient
open Microsoft.SqlServer.TransactSql.ScriptDom

type SqlCommand with
    member this.AsyncExecuteReader behavior =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

let defaultCommandTimeout = (new SqlCommand()).CommandTimeout

module SqlDataReader = 

    let internal map mapping (reader: SqlDataReader) = 
        reader |> Seq.unfold (fun current -> if current.Read() then Some( mapping reader, current) else None) 

    let internal getOption<'a> (key: string) (reader: SqlDataReader) =
        let v = reader.[key] 
        if Convert.IsDBNull v then None else Some(unbox<'a> v)

    let internal getValueOrDefault<'a> (key: string) defaultValue (reader: SqlDataReader) =
        let v = reader.[key] 
        if Convert.IsDBNull v then defaultValue else unbox<'a> v

        
let DbNull = box DBNull.Value

type Column = {
    Name: string
    Ordinal: int
    TypeInfo: TypeInfo
    IsNullable: bool
    MaxLength: int
    ReadOnly: bool
    Identity: bool
}   with
    member this.ClrTypeConsideringNullable = 
        if this.IsNullable 
        then typedefof<_ option>.MakeGenericType this.TypeInfo.ClrType
        else this.TypeInfo.ClrType

and TypeInfo = {
    TypeName: string
    Schema: string
    SqlEngineTypeId: int
    UserTypeId: int
    SqlDbTypeId: int
    IsFixedLength: bool option
    ClrTypeFullName: string
    UdttName: string 
    TableTypeColumns: Column seq
}   with
    member this.SqlDbType : SqlDbType = enum this.SqlDbTypeId
    member this.ClrType : Type = Type.GetType this.ClrTypeFullName
    member this.TableType = this.SqlDbType = SqlDbType.Structured
    member this.IsValueType = not this.TableType && this.ClrType.IsValueType

type Parameter = {
    Name: string
    TypeInfo: TypeInfo
    Direction: ParameterDirection 
    DefaultValue: obj option
}

let internal dataTypeMappings = Dictionary<string, TypeInfo[]>()

let internal findTypeInfoBySqlEngineTypeId (connStr, system_type_id, user_type_id : int option) = 
    assert (dataTypeMappings.ContainsKey connStr)

    dataTypeMappings.[connStr] 
    |> Array.filter(fun x -> 
        let result = 
            x.SqlEngineTypeId = system_type_id &&
            (user_type_id.IsSome && x.UserTypeId = user_type_id.Value || user_type_id.IsNone && x.UserTypeId = system_type_id)
        result
    ) 
    |> Seq.exactlyOne

let internal findTypeInfoByProviderType( connStr, sqlDbType)  = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.find (fun x -> x.SqlDbType = sqlDbType)

let rec parseDefaultValue (definition: string) (expr: ScalarExpression) = 
    match expr with
    | :? Literal as x ->
        match x.LiteralType with
        | LiteralType.Default | LiteralType.Null -> Some null
        | LiteralType.Integer -> x.Value |> int |> box |> Some
        | LiteralType.Money | LiteralType.Numeric -> x.Value |> decimal |> box |> Some
        | LiteralType.Real -> x.Value |> float |> box |> Some 
        | _ -> None
    | :? UnaryExpression as x when x.UnaryExpressionType <> UnaryExpressionType.BitwiseNot ->
        let fragment = definition.Substring( x.StartOffset, x.FragmentLength)
        match x.Expression with
        | :? Literal as x ->
            match x.LiteralType with
            | LiteralType.Integer -> fragment |> int |> box |> Some
            | LiteralType.Money | LiteralType.Numeric -> fragment |> decimal |> box |> Some
            | LiteralType.Real -> fragment |> float |> box |> Some 
            | _ -> None
        | _  -> None 
    | _ -> None 

type Routine = 
    | StoredProcedure of schema: string * name: string
    | TableValuedFunction of schema: string * name: string 
    | ScalarValuedFunction of schema: string * name: string

    member this.Name = 
        match this with
        | StoredProcedure(_, name) | TableValuedFunction(_, name) | ScalarValuedFunction(_, name) -> name

    member this.TwoPartName = 
        match this with
        | StoredProcedure(schema, name) | TableValuedFunction(schema, name) | ScalarValuedFunction(schema, name) -> sprintf "%s.%s" schema name

    member this.IsStoredProc = match this with StoredProcedure _ -> true | _ -> false
    
    member this.CommantText(parameters: Parameter list) = 
        match this with 
        | StoredProcedure(schema, name) -> this.TwoPartName
        | TableValuedFunction(schema, name) -> 
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT * FROM %s(%s)" this.TwoPartName
        | ScalarValuedFunction(schema, name) ->     
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT %s(%s)" this.TwoPartName

type SqlConnection with

 //address an issue when regular Dispose on SqlConnection needed for async computation 
 //wipes out all properties like ConnectionString in addition to closing connection to db
    member this.UseLocally() =
        if this.State = ConnectionState.Closed then 
            this.Open()
            { new IDisposable with member __.Dispose() = this.Close() }
        else { new IDisposable with member __.Dispose() = () }
    
    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member this.IsSqlAzure = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("SELECT SERVERPROPERTY('edition')", this)
        cmd.ExecuteScalar().Equals("SQL Azure")

    member internal this.GetUserSchemas() = 
        use __ = this.UseLocally()
        use cmd = new SqlCommand("SELECT name FROM SYS.SCHEMAS WHERE principal_id = 1", this)
        use reader = cmd.ExecuteReader()
        reader |> SqlDataReader.map (fun record -> record.GetString(0)) |> Seq.toList

    member internal this.GetRoutines( schema) = 
        assert (this.State = ConnectionState.Open)
        let getRoutinesQuery = sprintf "SELECT ROUTINE_SCHEMA, ROUTINE_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = '%s'" schema
        use cmd = new SqlCommand(getRoutinesQuery, this)
        use reader = cmd.ExecuteReader()
        reader 
        |> SqlDataReader.map (fun x -> 
            let schema, name = unbox x.["ROUTINE_SCHEMA"], unbox x.["ROUTINE_NAME"]
            let dataType = x.["DATA_TYPE"]
            match x.["DATA_TYPE"] with
            | :? string as x when x = "TABLE" -> TableValuedFunction(schema, name)
            | :? DBNull -> StoredProcedure(schema, name)
            | _ -> ScalarValuedFunction(schema, name)
        ) 
        |> Seq.toArray
            
    member internal this.GetParameters( routine: Routine) =      
        assert (this.State = ConnectionState.Open)
        let bodyAndParamsInfoQuery = sprintf "
            -- get body 
            EXEC sp_helptext '%s'; 
            -- get params info
            SELECT 
	            ps.PARAMETER_NAME AS name
	            ,CAST(ts.system_type_id AS INT) AS suggested_system_type_id
	            ,ts.user_type_id AS suggested_user_type_id
	            ,CONVERT(BIT, CASE ps.PARAMETER_MODE WHEN 'INOUT' THEN 1 ELSE 0 END) AS suggested_is_output
	            ,CONVERT(BIT, CASE ps.PARAMETER_MODE WHEN 'IN' THEN 1 WHEN 'INOUT' THEN 1 ELSE 0 END) AS suggested_is_input 
            FROM INFORMATION_SCHEMA.PARAMETERS AS ps
	            JOIN sys.types AS ts ON (ps.PARAMETER_NAME <> '' AND ps.DATA_TYPE = ts.name OR (ps.DATA_TYPE = 'table type' AND ps.USER_DEFINED_TYPE_NAME = ts.name))
            WHERE SPECIFIC_CATALOG = db_name() AND CONCAT(SPECIFIC_SCHEMA,'.',SPECIFIC_NAME) = '%s'
            ORDER BY ORDINAL_POSITION" routine.TwoPartName routine.TwoPartName

        use getBodyAndParamsInfo = new SqlCommand( bodyAndParamsInfoQuery, this)
        use reader = getBodyAndParamsInfo.ExecuteReader()
        let spDefinition = 
            reader |> SqlDataReader.map (fun x -> x.GetString (0)) |> String.concat ""

        let paramDefaults = Task.Factory.StartNew( fun() ->

            let parser = TSql110Parser( true)
            let tsqlReader = new StringReader(spDefinition)
            let errors = ref Unchecked.defaultof<_>
            let fragment = parser.Parse(tsqlReader, errors)

            let result = Dictionary()

            fragment.Accept {
                new TSqlFragmentVisitor() with
                    member __.Visit(node : ProcedureParameter) = 
                        base.Visit node
                        result.[node.VariableName.Value] <- parseDefaultValue spDefinition node.Value
            }

            result
        )

        let paramsDataAvailable = reader.NextResult()
        assert paramsDataAvailable
        reader |> SqlDataReader.map (fun record -> 
            let name = string record.["name"]
            let direction = 
                if unbox record.["suggested_is_output"]
                then 
                    invalidArg name "Output parameters are not supported"
                else 
                    assert(unbox record.["suggested_is_input"])
                    ParameterDirection.Input 

            let system_type_id: int = unbox record.["suggested_system_type_id"]
            let user_type_id = record |> SqlDataReader.getOption "suggested_user_type_id"

            let typeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, system_type_id, user_type_id)

            let keys = paramDefaults.Result.Keys

            { 
                Name = name 
                TypeInfo = typeInfo
                Direction = direction 
                DefaultValue = paramDefaults.Result.[name] 
            }
        )
        |> Seq.toList

    member internal this.GetTables( schema) = [
        assert (this.State = ConnectionState.Open)
        let getTablesQuery = sprintf "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = '%s'" schema
        use cmd = new SqlCommand(getTablesQuery, this)
        use reader = cmd.ExecuteReader()
        while reader.Read() do
            yield reader.GetString(0)
    ]

    member internal this.GetFullQualityColumnInfo commandText = [
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()

        while reader.Read() do
            let user_type_id = reader |> SqlDataReader.getOption<int> "user_type_id"
            let system_type_id = reader.["system_type_id"] |> unbox<int>

            let x = { 
                Column.Name = string reader.["name"]
                Ordinal = unbox reader.["column_ordinal"]
                TypeInfo = findTypeInfoBySqlEngineTypeId (this.ConnectionString, system_type_id, user_type_id)
                IsNullable = unbox reader.["is_nullable"]
                MaxLength = reader.["max_length"] |> unbox<int16> |> int
                ReadOnly = not( SqlDataReader.getValueOrDefault "is_updateable" true reader)
                Identity = SqlDataReader.getValueOrDefault "is_identity_column" false reader 
            }
            yield x 
    ] 

    member internal this.FallbackToSETFMONLY(commandText, commandType, parameters: Parameter list) = 
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
                            findTypeInfoByProviderType(this.ConnectionString, t)
                        IsNullable = unbox row.["AllowDBNull"]
                        MaxLength = unbox row.["ColumnSize"]
                        ReadOnly = unbox row.["IsAutoIncrement"] || unbox row.["IsReadOnly"]
                        Identity = unbox row.["IsAutoIncrement"]
                    }
            ]

    member internal this.LoadDataTypesMap() = 
        if not <| dataTypeMappings.ContainsKey this.ConnectionString
        then
            assert (this.State = ConnectionState.Open)
            let providerTypes = 
                dict [| 
                    for row in this.GetSchema("DataTypes").Rows do
                        let fullTypeName = string row.["TypeName"]
                        let typeName, clrType = 
                            match fullTypeName.Split(',') |> List.ofArray with
                            | [name] -> name, string row.["DataType"]
                            | name::_ -> name, fullTypeName
                            | [] -> failwith "Unaccessible"

                        let isFixedLength = 
                            if row.IsNull("IsFixedLength") 
                            then None 
                            else row.["IsFixedLength"] |> unbox |> Some

                        let providedType = unbox row.["ProviderDbType"]
                        if providedType <> int SqlDbType.Structured
                        then 
                            yield typeName, (providedType, clrType, isFixedLength)
                |]

            let sqlEngineTypes = [|
                use cmd = new SqlCommand("
                    SELECT t.name, ISNULL(assembly_class, t.name) as full_name, t.system_type_id, t.user_type_id, t.is_table_type, s.name as schema_name, t.is_user_defined
                    FROM sys.types AS t
	                    JOIN sys.schemas AS s ON t.schema_id = s.schema_id
	                    LEFT JOIN sys.assembly_types ON t.user_type_id = sys.assembly_types.user_type_id
                    ", this) 
                use reader = cmd.ExecuteReader()
                while reader.Read() do
                    yield 
                        string reader.["name"], 
                        string reader.["full_name"], 
                        unbox<byte> reader.["system_type_id"] |> int, 
                        unbox<int> reader.["user_type_id"], 
                        unbox reader.["is_table_type"], 
                        string reader.["schema_name"], 
                        unbox reader.["is_user_defined"]
            |]

            let typeInfos = [|
                for name, full_name, system_type_id, user_type_id, is_table_type, schema_name, is_user_defined in sqlEngineTypes do
                    let providerdbtype, clrType, isFixedLength = 
                        match providerTypes.TryGetValue(full_name) with
                        | true, value -> value
                        | false, _ when full_name = "sysname" -> 
                            providerTypes.["nvarchar"]
                        | false, _ when is_user_defined && not is_table_type ->
                            let system_type_name = 
                                sqlEngineTypes 
                                |> Array.pick (fun (typename', _, system_type_id', _, _, _, is_user_defined') -> if system_type_id = system_type_id' && not is_user_defined' then Some typename' else None)
                            providerTypes.[system_type_name]
                        | false, _ when is_table_type -> 
                            int SqlDbType.Structured, "", None
                        | _ -> failwith ("Unexpected type: " + full_name)

                    let clrTypeFixed = if system_type_id = 48 (*tinyint*) then typeof<byte>.FullName else clrType

                    let columns = 
                        if is_table_type
                        then
                            seq {
                                use cmd = new SqlCommand("
                                    SELECT c.name, c.column_id, c.system_type_id, c.user_type_id, c.is_nullable, c.max_length, c.is_identity, c.is_computed
                                    FROM sys.table_types AS tt
                                    INNER JOIN sys.columns AS c ON tt.type_table_object_id = c.object_id
                                    WHERE tt.user_type_id = @user_type_id
                                    ORDER BY column_id")
                                cmd.Parameters.AddWithValue("@user_type_id", user_type_id) |> ignore
                                use closeConn = this.UseLocally()
                                cmd.Connection <- this
                                use reader = cmd.ExecuteReader()
                                while reader.Read() do 
                                    let user_type_id = reader |> SqlDataReader.getOption "user_type_id"
                                    let stid = reader.["system_type_id"] |> unbox<byte> |> int
                                    yield {
                                        Column.Name = string reader.["name"]
                                        Ordinal = unbox reader.["column_id"]
                                        TypeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, stid, user_type_id)
                                        IsNullable = unbox reader.["is_nullable"]
                                        MaxLength = reader.["max_length"] |> unbox<int16> |> int
                                        ReadOnly = unbox reader.["is_identity"] || unbox reader.["is_computed"]
                                        Identity = unbox reader.["is_identity"] 
                                    }
                            } 
                            |> Seq.cache
                        else
                            Seq.empty

                    yield {
                        TypeName = name
                        Schema = schema_name
                        SqlEngineTypeId = system_type_id
                        UserTypeId = user_type_id
                        SqlDbTypeId = providerdbtype
                        IsFixedLength = isFixedLength
                        ClrTypeFullName = clrTypeFixed
                        UdttName = if is_table_type then full_name else ""
                        TableTypeColumns = columns
                    }
            |]

            dataTypeMappings.Add( this.ConnectionString, typeInfos)

    member this.ClearDataTypesMap() = 
        dataTypeMappings.Clear()
