[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Data
open System.Data.SqlClient
open Microsoft.FSharp.Reflection

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

let internal finBySqlEngineTypeId id = 
    !dataTypeMappings |> List.tryFind(fun x -> x.SqlEngineTypeId = id )

let internal finBySqlEngineTypeIdAndUdt(id, udtName) = 
    !dataTypeMappings |> List.tryFind(fun x -> x.SqlEngineTypeId = id && x.UdtName = udtName)
    
let internal findTypeInfoByProviderType(sqlDbType, udtName)  = 
    !dataTypeMappings  
    |> List.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdtName = udtName)

let internal getTupleTypeForColumns (xs : seq<Column>) = 
    match Seq.toArray xs with
    | [| x |] -> x.ClrTypeConsideringNullable
    | xs' -> FSharpType.MakeTupleType [| for x in xs' -> x.ClrTypeConsideringNullable|]

type SqlConnection with

    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member internal this.GetDataTypesMapping() = 
        assert (this.State = ConnectionState.Open)
        let providerTypes = [| 
            for row in this.GetSchema("DataTypes").Rows -> 
                string row.["TypeName"],  unbox<int> row.["ProviderDbType"], string row.["DataType"]
        |]

        let sqlEngineTypes = [|
            use cmd = new SqlCommand("SELECT name, system_type_id, user_type_id, is_table_type FROM sys.types", this) 
            use reader = cmd.ExecuteReader()
            while reader.Read() do
                yield reader.GetString(0), reader.GetByte(1) |> int, reader.GetInt32(2), reader.GetBoolean(3)
        |]

        let connectionString = this.ConnectionString
        query {
            for typename, providerdbtype, clrType in providerTypes do
            join (name, system_type_id, user_type_id, is_table_type) in sqlEngineTypes on (typename = name)
            //the next line fix the issue when ADO.NET SQL provider maps tinyint to byte despite of claiming to map it to SByte according to GetSchema("DataTypes")
            let clrTypeFixed = if system_type_id = 48 (*tinyint*) then typeof<byte>.FullName else clrType
            let tvpColumns = 
                if system_type_id = 243 && is_table_type
                then
                    seq {
                        use cmd = new SqlCommand("
                            SELECT c.name, c.column_id, c.system_type_id, c.is_nullable
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
                                ClrTypeFullName = reader.["system_type_id"] |> unbox<byte> |> int |> finBySqlEngineTypeId |> Option.map (fun x -> x.ClrTypeFullName) |> Option.get
                                IsNullable = unbox reader.["is_nullable"]
                            }
                    } 
                    |> Seq.cache
                else
                    Seq.empty

            select {
                SqlEngineTypeId = system_type_id
                SqlDbTypeId = providerdbtype
                ClrTypeFullName = clrTypeFixed
                UdtName = if is_table_type then name else ""
                TvpColumns = tvpColumns
            }
        }
        |> Seq.toList

    member internal this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings 
        then
            dataTypeMappings := this.GetDataTypesMapping()

