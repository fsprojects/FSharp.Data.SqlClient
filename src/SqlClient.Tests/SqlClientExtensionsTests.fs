namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open Xunit
open System.Diagnostics

open FSharp.Data

type ExtensionsTest() = 
    
    let conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    do
        conn.Open()
        conn.LoadDataTypesMap()

//    interface IDisposable with
//        member __.Dispose() = 
//            conn.ClearDataTypesMap()

    [<Fact>]
//    member  __.TestUDTTs() =
//        UDTTs( conn.ConnectionString) 
//        |> Seq.collect (fun x -> x.TvpColumns)
//        |> Seq.iter (printfn "%A")    
//
//    [<Fact>]
//    member  __.AllTypes() =
//        SqlClrTypes( conn.ConnectionString) 
//        |> Seq.groupBy(fun t->t.SqlEngineTypeId)
//        |> Seq.map (sprintf "%A")
//        |> Seq.iter Debug.WriteLine

    member  __.GetFullQualityColumnInfo() =
        conn.GetFullQualityColumnInfo("dbo.uspGetWhereUsedProductID") 
        |> Seq.map (sprintf "%A")
        |> Seq.iter Debug.WriteLine

    [<Fact>]
    member  __.GetAllSPs() =
        conn.GetRoutines("dbo")
        |> Seq.map (sprintf "%A")
        |> Seq.iter Debug.WriteLine

    //[<Fact(Skip = "Until we gen SQL Azure with permissions")>]
//    [<Fact>]
//    member  __. GetParameters() =
//        conn.GetParameters( StoredProcedure("dbo", "Swap"))
//        |> Seq.map (sprintf "%A")
//        |> Seq.iter Debug.WriteLine

    [<Fact>]
    member  __.``UDT Lookup``() = 
        let system_type_id = 231
        let user_type_id = 257
        let t = Extensions.findTypeInfoBySqlEngineTypeId(conn.ConnectionString, system_type_id, Some user_type_id)
        Assert.Equal(typeof<string>, t.ClrType)
        Assert.Equal<string>("dbo", t.Schema)
        Assert.Equal(SqlDbType.NVarChar, t.SqlDbType)
        Assert.Equal(system_type_id, t.SqlEngineTypeId)
        Assert.Equal(user_type_id, t.UserTypeId)
        Assert.Empty(t.UdttName)