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

    member this.ExecuteReaderWith(f: IDataRecord -> 'a) = seq {
       use reader = this.ExecuteReader()
       while reader.Read() do
          yield f(reader)
    }

    //address an issue when regular Dispose on SqlConnection needed for async computation wipes out all properties like ConnectionString in addition to closing connection to db
    member this.CloseConnectionOnly() = {
        new IDisposable with
            member __.Dispose() = this.Connection.Close()
    }

let private dataTypeMappings = ref List.empty

type SqlConnection with
    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member this.GetDataTypesMapping() = 
        assert (this.State = ConnectionState.Open)
        let providerTypes = [| 
            for row in this.GetSchema("DataTypes").Rows -> 
                string row.["TypeName"],  unbox<int> row.["ProviderDbType"], string row.["DataType"]
        |]

        let sqlEngineTypes = [|
            use cmd = new SqlCommand("SELECT name, system_type_id FROM sys.types", this) 
            use reader = cmd.ExecuteReader()
            while reader.Read() do
                yield reader.GetString(0), reader.GetByte(1) |> int
        |]

        query {
            for typename, providerdbtype, clrType in providerTypes do
            join (systypename, systemtypeid) in sqlEngineTypes on (typename = systypename)
            //the next line fix the issue when ADO.NET SQL provider maps tinyint to byte despite of claiming to map it to SByte according to GetSchema("DataTypes")
            let clrTypeFixed = if systemtypeid = 48 (*tinyint*) then typeof<byte>.FullName else clrType
            select (systemtypeid, providerdbtype, clrTypeFixed)
        }
        |> Seq.toList

    member internal this.LoadDataTypesMap() = 
        if List.isEmpty !dataTypeMappings 
        then
            dataTypeMappings := this.GetDataTypesMapping()

let internal mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage) = 
    match !dataTypeMappings |> List.tryFind (fun(x, _, _) ->  x = sqlEngineTypeId) with
    | Some(_, sqlDbTypeId, clrTypeName) -> clrTypeName, sqlDbTypeId
    | None -> failwithf "Cannot map sql engine type %i to CLR/SqlDbType type. %s" sqlEngineTypeId detailedMessage

let internal findBySqlDbType sqlDbType  = 
    match !dataTypeMappings |> List.tryFind (fun(_, x, _) -> sqlDbType = enum<SqlDbType> x) with
    | Some(_, _, clrTypeName) -> clrTypeName
    | None -> failwithf "Cannot map SqlDbType %O to CLR type." sqlDbType

