[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Data
open System.Data.SqlClient

type SqlCommand with
    member this.AsyncExecuteReader(behavior : CommandBehavior) =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

    //address an issue and regular Dispose on connection needed for async computation wipes out all properties like ConnectionString in addition to closing connection to db
    member this.CloseConnectionOnly() = {
        new IDisposable with
            member __.Dispose() = this.Connection.Close()
    }

let private dataTypeMappings = ref List.empty

type SqlConnection with
    member internal this.CheckVersion() = 
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 or higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member internal this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings
        then
            dataTypeMappings :=
                let datatypes = 
                   this.GetSchema("DataTypes").AsEnumerable() 
                   |> Seq.map (fun r -> r.Field("TypeName") |> string, r.Field("ProviderDbType") |> int, r.Field("DataType") |> string)
                   |> Array.ofSeq 
                let systypes = 
                   use c = new SqlCommand("SELECT name, system_type_id FROM sys.types", this) in
                   c.ExecuteReader(CommandBehavior.CloseConnection)
                   |> Seq.cast<IDataRecord>
                   |> Seq.map (fun r -> r.["name"] |> string, r.["system_type_id"] |> unbox<byte> |> int)
                   |> Array.ofSeq
                query {
                  for typename, providerdbtype, datatype in datatypes do
                  join (systypename, systemtypeid) in systypes on (typename = systypename)
                  select (systemtypeid, providerdbtype, datatype)
            }
            |> Seq.toList


let internal mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage) = 
    match !dataTypeMappings |> List.tryFind (fun(x, _, _) ->  x = sqlEngineTypeId) with
    | Some(_, sqlDbTypeId, clrTypeName) -> clrTypeName, sqlDbTypeId
    | None -> failwithf "Cannot map sql engine type %i to CLR/SqlDbType type. %s" sqlEngineTypeId detailedMessage

let internal findBySqlDbType sqlDbType  = 
    match !dataTypeMappings |> List.tryFind (fun(_, x, _) -> sqlDbType = enum<SqlDbType> x) with
    | Some(_, _, clrTypeName) -> clrTypeName
    | None -> failwithf "Cannot map SqlDbType %O to CLR type." sqlDbType

