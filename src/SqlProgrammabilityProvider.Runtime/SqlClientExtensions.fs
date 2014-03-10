[<AutoOpen>]
module FSharp.Data.Experimental.Runtime.Sqlclient

open System
open System.Text
open System.Data
open System.Data.SqlClient
open Microsoft.FSharp.Reflection
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common

let DbNull = box DBNull.Value

type SqlCommand with

    member this.AsyncExecuteReader behavior =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

    //address an issue when regular Dispose on SqlConnection needed for async computation wipes out all properties like ConnectionString in addition to closing connection to db
    member this.CloseConnectionOnly() = {
        new IDisposable with
            member __.Dispose() = this.Connection.Close()
    }

let private dataTypeMappings = ref List.empty

let findTypeInfoBySqlEngineTypeId (systemId, userId : int option) = 
    match !dataTypeMappings 
            |> List.filter(fun x -> x.SqlEngineTypeId = systemId ) with
    | [v] -> v
    | list -> match list |> List.filter(fun x -> userId.IsNone || x.UserTypeId = userId.Value) with
              | [v] -> v
              | _-> failwithf "error resolving systemid %A and userid %A" systemId userId

let UDTTs() = 
    !dataTypeMappings |> List.filter(fun x -> x.TableType ) |> Array.ofList

let SqlClrTypes() = 
    !dataTypeMappings |> Array.ofList
    
let findTypeInfoByProviderType(sqlDbType, udttName)  = 
    !dataTypeMappings  
    |> List.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdttName = udttName)

let findTypeInfoByName(name) = 
    !dataTypeMappings |> List.find(fun x -> x.TypeName = name || x.UdttName = name)

let splitName (twoPartsName : string) =
   let parts = twoPartsName.Split('.')
   parts.[0], parts.[1]
     
let ReturnValue() = { 
    Name = "@ReturnValue" 
    Direction = ParameterDirection.ReturnValue
    TypeInfo = findTypeInfoByName "int" 
    DefaultValue = "" }

type SqlDataReader with
    
    member this.toOption<'a> (key:string) =
        let v = this.[key] 
        if v = DbNull then None else Some(unbox<'a> v)

type DataRow with

    member this.toOption<'a> (key:string) = 
        if this.IsNull(key) then None else Some(unbox<'a> this.[key])

type SqlConnection with

    member this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member this.FallbackToSETFMONLY(commandText, sqlParameters) = 
        assert (this.State = ConnectionState.Open)
        
        use cmd = new SqlCommand(commandText, this, CommandType = CommandType.StoredProcedure)
        for p in sqlParameters do
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
                            findTypeInfoByProviderType(unbox t, "").Value
                        IsNullable = unbox row.["AllowDBNull"]
                        MaxLength = unbox row.["ColumnSize"]
                    }
            ]

    member this.GetFullQualityColumnInfo commandText = [
        assert (this.State = ConnectionState.Open)
        
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        use reader = cmd.ExecuteReader()

        while reader.Read() do
            let utid = reader.toOption<int> "user_type_id"
            let stid = reader.["system_type_id"] |> unbox<int>
            yield { 
                Column.Name = string reader.["name"]
                Ordinal = unbox reader.["column_ordinal"]
                TypeInfo = findTypeInfoBySqlEngineTypeId (stid, utid)
                IsNullable = unbox reader.["is_nullable"]
                MaxLength = reader.["max_length"] |> unbox<int16> |> int
            }
    ] 

    member this.GetDefaults(twoPartsName : string, isFunction) =         
        assert (this.State = ConnectionState.Open)
        let db  = Server( ServerConnection(this)).Databases.[this.Database]
        let schema, name =  splitName twoPartsName
        let coll = if isFunction 
                   then db.UserDefinedFunctions.[name, schema].Parameters :> ParameterCollectionBase 
                   else db.StoredProcedures.[name, schema].Parameters :> ParameterCollectionBase 
        seq {for p in coll |> Seq.cast<Parameter> -> p.Name, p.DefaultValue } |> Map.ofSeq

    member this.GetFunctionColumns(twoPartsName : string) = 
        let db  = Server( ServerConnection(this)).Databases.[this.Database]
        let schema, name =  splitName twoPartsName
        let types = db.UserDefinedDataTypes |> Seq.cast<UserDefinedDataType> |> Seq.map(fun t -> t.Name, t.SystemType) |> Map.ofSeq
        db.UserDefinedFunctions.[name, schema].Columns
        |> Seq.cast<Column>
        |> Seq.mapi ( fun i c ->
                let dataType = defaultArg (types.TryFind(c.DataType.Name)) c.DataType.Name
                {
                    Name = c.Name 
                    TypeInfo =  findTypeInfoByName dataType
                    IsNullable = c.Nullable
                    Ordinal = i
                    MaxLength = c.DataType.MaximumLength
                })
        |> List.ofSeq

    member this.GetParameters(twoPartsName, isFunction) =         
        assert (this.State = ConnectionState.Open)
        let defaults = this.GetDefaults(twoPartsName, isFunction)
        [
            for r in this.GetSchema("ProcedureParameters").Rows do
                let fullName = sprintf "%s.%s" (string r.["specific_schema"]) (string r.["specific_name"]) 
                if  string r.["specific_catalog"] = this.Database && fullName = twoPartsName then
                    let name = string r.["parameter_name"]
                    let direction = match string r.["parameter_mode"] with 
                                    | "IN" -> ParameterDirection.Input 
                                    | "OUT" -> ParameterDirection.Output
                                    | "INOUT" -> ParameterDirection.InputOutput
                                    | _ -> failwithf "Parameter %s has unsupported direction %O" name r.["parameter_mode"]
                    let udt = string r.["data_type"]
                    yield { 
                        Name = name 
                        TypeInfo = findTypeInfoByName(udt) 
                        Direction = direction 
                        DefaultValue = defaultArg (defaults.TryFind(name)) ""}
        ]

    member this.GetFunctions() = 
        assert (this.State = ConnectionState.Open)
        let db  = Server( ServerConnection(this)).Databases.[this.Database]
        [ 
            for f in db.UserDefinedFunctions do
                if not f.IsSystemObject && f.DataType = null then 
                    yield sprintf "%s.%s" f.Schema f.Name
        ]

    member this.GetProcedures() = 
        assert (this.State = ConnectionState.Open)
        [ 
            for r in this.GetSchema("Procedures").Rows do
                if string r.["routine_type"] = "PROCEDURE" then 
                    yield sprintf "%s.%s" (string r.["specific_schema"]) (string r.["specific_name"])
        ]
    
    member this.GetDataTypesMapping() = 
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
            use cmd = new SqlCommand("SELECT t.name, ISNULL(assembly_class, t.name), t.system_type_id, t.user_type_id, t.is_table_type
                                        FROM sys.types t
                                        left join sys.assembly_types a on t.user_type_id = a.user_type_id ", this) 
            use reader = cmd.ExecuteReader()
            while reader.Read() do
                yield reader.GetString(0), reader.GetString(1), reader.GetByte(2) |> int, reader.GetInt32(3), reader.GetBoolean(4)
        |]

        let connectionString = this.ConnectionString
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
                        use conn = new SqlConnection(connectionString) 
                        conn.Open()
                        cmd.Connection <- conn
                        use reader = cmd.ExecuteReader()
                        while reader.Read() do 
                            let utid = reader.toOption "user_type_id"
                            let stid = reader.["system_type_id"] |> unbox<byte> |> int
                            yield {
                                Column.Name = string reader.["name"]
                                Ordinal = unbox reader.["column_id"]
                                TypeInfo = findTypeInfoBySqlEngineTypeId(stid, utid)
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
        |> Seq.toList

    member this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings then
            dataTypeMappings := this.GetDataTypesMapping()



