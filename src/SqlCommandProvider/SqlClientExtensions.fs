[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Data.Experimental.Internals.SqlClientExtensions

open System
open System.Data
open System.Data.SqlClient
open Microsoft.FSharp.Reflection

type SqlCommand with

    member this.MapNullParams() =
        for p in this.Parameters do
            if p.Value = null then p.Value <- DBNull.Value

    member this.AsyncExecuteReader behavior =
        this.MapNullParams()
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        this.MapNullParams()
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

    //address an issue when regular Dispose on SqlConnection needed for async computation wipes out all properties like ConnectionString in addition to closing connection to db
    member this.CloseConnectionOnly() = {
        new IDisposable with
            member __.Dispose() = this.Connection.Close()
    }

let private dataTypeMappings = ref List.empty

let internal findTypeInfoBySqlEngineTypeId id = 
    !dataTypeMappings |> List.filter(fun x -> x.SqlEngineTypeId = id ) |> Seq.exactlyOne 

let internal findBySqlEngineTypeIdAndUdt(id, udttName) = 
    !dataTypeMappings |> List.tryFind(fun x -> x.SqlEngineTypeId = id && (not x.TableType || x.UdttName = udttName))
    
let internal findTypeInfoByProviderType(sqlDbType, udttName)  = 
    !dataTypeMappings  
    |> List.tryFind (fun x -> x.SqlDbType = sqlDbType && x.UdttName = udttName)

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
                SqlEngineTypeId = system_type_id
                SqlDbTypeId = providerdbtype
                IsFixedLength = isFixedLength
                ClrTypeFullName = clrTypeFixed
                UdttName = if is_table_type then name else ""
                TvpColumns = tvpColumns
            }
        }
        |> Seq.toList

    member internal this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings 
        then
            dataTypeMappings := this.GetDataTypesMapping()

type SqlParameter with
    member internal this.ToParameter() = 
        let udt = if String.IsNullOrEmpty(this.TypeName) then "" else this.TypeName.Split('.') |> Seq.last
        match findTypeInfoByProviderType(this.SqlDbType, udt) with
        | Some x -> 
            { Name = this.ParameterName; TypeInfo = x; Direction = this.Direction }
        | None -> 
            failwithf "Cannot map pair of SqlDbType '%O' and user definto type '%s' CLR type." this.SqlDbType udt
