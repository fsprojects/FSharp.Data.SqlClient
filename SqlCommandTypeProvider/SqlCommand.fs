[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Data.SqlClient.Extensions

open System.Data
open System.Data.SqlClient

type SqlCommand with
    member this.AsyncExecuteReader(behavior : CommandBehavior) =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

let private dataTypeMappings = ref List.empty

type SqlConnection with
    member internal this.CheckVersion() = 
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 or higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member internal this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings
        then
            dataTypeMappings := query {
                let getSysTypes = new SqlCommand("SELECT * FROM sys.types", this)
                for x in this.GetSchema("DataTypes").AsEnumerable() do
                join y in (getSysTypes.ExecuteReader(CommandBehavior.CloseConnection) |> Seq.cast<IDataRecord>) on 
                    (x.Field("TypeName") = string y.["name"])
                let sqlEngineTypeId = y.["system_type_id"] |> unbox<byte> |> int
                let sqlDbTypeId : int = x.Field("ProviderDbType")
                let clrTypeName : string = x.Field("DataType")
                select(sqlEngineTypeId, sqlDbTypeId, clrTypeName)
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

