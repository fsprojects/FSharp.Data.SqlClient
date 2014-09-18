[<AutoOpen>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Text
open System.Data
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open System.Data.SqlClient
open Microsoft.FSharp.Reflection
open Microsoft.SqlServer.TransactSql.ScriptDom

type SqlCommand with
    member this.AsyncExecuteReader behavior =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

type DataRow with
    member this.toOption<'a> (key:string) = if this.IsNull(key) then None else Some(unbox<'a> this.[key])

module SqlDataReader = 

    let internal map mapping (reader: SqlDataReader) = 
        reader |> Seq.unfold (fun current -> if current.Read() then Some( mapping reader, current) else None) 

    let internal getOption<'a> (key: string) (reader: SqlDataReader) =
        let v = reader.[key] 
        if DBNull.Value.Equals(v) then None else Some(unbox<'a> v)
        
let DbNull = box DBNull.Value

type Column = {
    Name : string
    Ordinal : int
    TypeInfo : TypeInfo
    IsNullable : bool
    MaxLength : int
}   with
    member this.ClrTypeConsideringNullable = 
        if this.IsNullable 
        then typedefof<_ option>.MakeGenericType this.TypeInfo.ClrType
        else this.TypeInfo.ClrType

and TypeInfo = {
    TypeName : string
    SqlEngineTypeId : int
    UserTypeId : int
    SqlDbTypeId : int
    IsFixedLength : bool option
    ClrTypeFullName : string
    UdttName : string 
    TvpColumns : Column seq
}   with
    member this.SqlDbType : SqlDbType = enum this.SqlDbTypeId
    member this.ClrType : Type = Type.GetType this.ClrTypeFullName
    member this.TableType = this.SqlDbType = SqlDbType.Structured
    member this.IsValueType = not this.TableType && this.ClrType.IsValueType

type Parameter = {
    Name : string
    TypeInfo : TypeInfo
    Direction : ParameterDirection 
    DefaultValue : obj option
}

let internal dataTypeMappings = Dictionary<string, TypeInfo[]>()

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

let internal findTypeInfoByProviderType( connStr, sqlDbType, udttName)  = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdttName = udttName)

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

type RoutineType = | StoredProcedure | TableValuedFunction | ScalarValuedFunction 

type Routine = {
    Schema: string
    Name: string 
    Type: RoutineType
}   with 

    member this.TwoPartName = sprintf "%s.%s" this.Schema this.Name

    member this.CommantText(parameters: Parameter list) = 
        match this.Type with 
        | StoredProcedure -> this.TwoPartName
        | TableValuedFunction -> 
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT * FROM %s(%s)" this.TwoPartName
        | ScalarValuedFunction ->     
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

    member internal this.GetRoutines() = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("SELECT ROUTINE_SCHEMA, ROUTINE_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.ROUTINES", this)
        use reader = cmd.ExecuteReader()
        reader 
        |> SqlDataReader.map (fun x -> 
            let schema, name = unbox x.["ROUTINE_SCHEMA"], unbox x.["ROUTINE_NAME"]
            let dataType = x.["DATA_TYPE"]
            match x.["DATA_TYPE"] with
            | :? string as x when x = "TABLE" -> { Name = name; Schema = schema; Type = TableValuedFunction }
            | :? DBNull -> { Name = name; Schema = schema; Type = StoredProcedure }
            | _ -> { Name = name; Schema = schema; Type = ScalarValuedFunction } 
        ) 
        |> Seq.toArray
            
    member internal this.GetParameters( twoPartName) =      
        assert (this.State = ConnectionState.Open)

        let bodyAndParamsInfoQuery = sprintf "
            -- get body 
            EXEC sp_helptext '%s'; 
            -- get params info
            SELECT 
	            ps.PARAMETER_NAME AS name
	            ,CAST(ts.system_type_id AS INT) AS suggested_system_type_id
	            ,ps.USER_DEFINED_TYPE_NAME AS suggested_user_type_name
	            ,CONVERT(BIT, CASE ps.PARAMETER_MODE WHEN 'INOUT' THEN 1 ELSE 0 END) AS suggested_is_output
	            ,CONVERT(BIT, CASE ps.PARAMETER_MODE WHEN 'IN' THEN 1 WHEN 'INOUT' THEN 1 ELSE 0 END) AS suggested_is_input 
            FROM INFORMATION_SCHEMA.PARAMETERS AS ps
	            JOIN sys.types AS ts ON (ps.PARAMETER_NAME <> '' AND ps.DATA_TYPE = ts.name OR (ps.DATA_TYPE = 'table type' AND ps.USER_DEFINED_TYPE_NAME = ts.name))
            WHERE SPECIFIC_CATALOG = db_name() AND CONCAT(SPECIFIC_SCHEMA,'.',SPECIFIC_NAME) = '%s'
            ORDER BY ORDINAL_POSITION" twoPartName twoPartName

        use getBodyAndParamsInfo = new SqlCommand( bodyAndParamsInfoQuery, this)
        use reader = getBodyAndParamsInfo.ExecuteReader()
        let spDefinition = 
            reader |> SqlDataReader.map (fun x -> x.GetString (0)) |> String.concat "\n"

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
                match unbox record.["suggested_is_input"], unbox record.["suggested_is_output"] with 
                | true, false -> ParameterDirection.Input 
                | true, true -> ParameterDirection.InputOutput
                | input, output -> failwithf "Parameter %s has unsupported direction %b/%b" name input output

            let system_type_id: int = unbox record.["suggested_system_type_id"]
            let user_type_name: string = string record.["suggested_user_type_name"]
            { 
                Name = name 
                TypeInfo = findBySqlEngineTypeIdAndUdtt( this.ConnectionString, system_type_id, user_type_name).Value
                Direction = direction 
                DefaultValue = paramDefaults.Result.[name] 
            }
        )
        |> Seq.toList

    member internal this.GetFullQualityColumnInfo commandText = [
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()

        while reader.Read() do
            let user_type_id = reader |> SqlDataReader.getOption<int> "user_type_id"
            let system_type_id = reader.["system_type_id"] |> unbox<int>
            yield { 
                Column.Name = string reader.["name"]
                Ordinal = unbox reader.["column_ordinal"]
                TypeInfo = findTypeInfoBySqlEngineTypeId (this.ConnectionString, system_type_id, user_type_id)
                IsNullable = unbox reader.["is_nullable"]
                MaxLength = reader.["max_length"] |> unbox<int16> |> int
            }
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
