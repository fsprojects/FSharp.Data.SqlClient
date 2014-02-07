[<AutoOpen>]
module FSharp.Data.Experimental.Runtime.Sqlclient

open System
open System.Data
open System.Data.SqlClient
open Microsoft.FSharp.Reflection
open Microsoft.SqlServer.Management.Smo

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

let findTypeInfoBySqlEngineTypeId id = 
    !dataTypeMappings |> List.filter(fun x -> x.SqlEngineTypeId = id ) |> Seq.exactlyOne 

let internal findBySqlEngineTypeIdAndUdt(id, udttName) = 
    !dataTypeMappings |> List.tryFind(fun x -> x.SqlEngineTypeId = id && (not x.TableType || x.UdttName = udttName))
    
let findTypeInfoByProviderType(sqlDbType, udttName)  = 
    !dataTypeMappings  
    |> List.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdttName = udttName)

let internal findTypeInfoByName(name) = 
    !dataTypeMappings |> List.tryFind(fun x -> x.TypeName = name || x.UdttName = name)

type SqlConnection with

    member this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member this.GetProcedures() = 
        assert (this.State = ConnectionState.Open)
        
        let fullName (r:DataRow) = sprintf "%s.%s" (string r.["specific_schema"]) (string r.["specific_name"])

        let parseParam (r:DataRow) =
            let name = string r.["parameter_name"]
            let direction = match string r.["parameter_mode"] with 
                            | "IN" -> ParameterDirection.Input 
                            | "OUT" -> ParameterDirection.Output
                            | "INOUT" -> ParameterDirection.InputOutput
                            | _ -> failwithf "Parameter %s has unsupported direction %O" name r.["parameter_mode"]
            let udt = string r.["data_type"]
            let param = findTypeInfoByName(udt) 
                        |> Option.map (fun x -> { Name = name; TypeInfo = x; Direction = direction })
            string r.["specific_catalog"], fullName r, param

        let rows = this.GetSchema("ProcedureParameters").Rows |> Seq.cast<DataRow> |> Seq.map parseParam
        
        let parameters = query { 
                            for catalog, name, param in rows do
                            where (catalog = this.Database)
                            groupBy name into g
                            let ps = [ for p,_,_ in g do if p.IsSome then yield p.Value ]
                            let unsupported = g |> Seq.exists (fun (p,_,_) -> p.IsNone || p.Value.Direction <> ParameterDirection.Input)
                            select (g.Key, (unsupported, ps))
                         } |> Map.ofSeq
        [ 
            for r in this.GetSchema("Procedures").Rows do
                let name = fullName r
                let isFunction = string r.["routine_type"] = "FUNCTION"
                match parameters.TryFind(name) with
                | Some (false, ps) -> yield name, isFunction, ps
                | None -> yield name, isFunction, []
                | _ -> ()

        ]

    member this.GetDataTypesMapping() = 
        assert (this.State = ConnectionState.Open)
        let providerTypes = [| 
            for row in this.GetSchema("DataTypes").Rows do
                let isFixedLength = if row.IsNull("IsFixedLength") then None else Some(unbox<bool> row.["IsFixedLength"])
                yield string row.["TypeName"],  unbox<int> row.["ProviderDbType"], string row.["DataType"], isFixedLength
        |]

        let sqlEngineTypes = [|
            use cmd = new SqlCommand("SELECT name, system_type_id, user_type_id, is_table_type FROM sys.types", this) 
            use reader = cmd.ExecuteReader()
            while reader.Read() do
                yield reader.GetString(0), reader.GetByte(1) |> int, reader.GetInt32(2), reader.GetBoolean(3)
        |]

        let connectionString = this.ConnectionString
        query {
            for typename, providerdbtype, clrType, isFixedLength in providerTypes do
            join (name, system_type_id, user_type_id, is_table_type) in sqlEngineTypes on (typename = name)
            //the next line fix the issue when ADO.NET SQL provider maps tinyint to byte despite of claiming to map it to SByte according to GetSchema("DataTypes")
            let clrTypeFixed = if system_type_id = 48 (*tinyint*) then typeof<byte>.FullName else clrType
            let tvpColumns = 
                if system_type_id = 243 && is_table_type
                then
                    seq {
                        use cmd = new SqlCommand("
                            SELECT c.name, c.column_id, c.system_type_id, c.is_nullable, c.max_length
                            FROM sys.table_types AS tt
                            INNER JOIN sys.columns AS c ON tt.type_table_object_id = c.object_id
                            WHERE tt.system_type_id = 243 AND tt.user_type_id = @user_type_id
                            ORDER BY column_id")
                        cmd.Parameters.AddWithValue("@user_type_id", user_type_id) |> ignore
                        use conn = new SqlConnection(connectionString) 
                        conn.Open()
                        cmd.Connection <- conn
                        use reader = cmd.ExecuteReader()
                        while reader.Read() do 
                            yield {
                                Column.Name = string reader.["name"]
                                Ordinal = unbox reader.["column_id"]
                                TypeInfo = reader.["system_type_id"] |> unbox<byte> |> int |> findTypeInfoBySqlEngineTypeId
                                IsNullable = unbox reader.["is_nullable"]
                                MaxLength = reader.["max_length"] |> unbox<int16> |> int
                            }
                    } 
                    |> Seq.cache
                else
                    Seq.empty

            select {
                TypeName = typename
                SqlEngineTypeId = system_type_id
                SqlDbTypeId = providerdbtype
                IsFixedLength = isFixedLength
                ClrTypeFullName = clrTypeFixed
                UdttName = if is_table_type then name else ""
                TvpColumns = tvpColumns
            }
        }
        |> Seq.toList

    member this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings 
        then
            dataTypeMappings := this.GetDataTypesMapping()


