namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open Xunit
open FsUnit.Xunit
open System.Diagnostics

open FSharp.Data.SqlClient

type ExtensionsTest() = 
    
    [<Literal>]
    let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
    //let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
    let conn = new SqlConnection(connectionString)
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

    member  __. GetFullQualityColumnInfo() =
        conn.GetFullQualityColumnInfo("dbo.uspGetWhereUsedProductID") 
        |> Seq.map (sprintf "%A")
        |> Seq.iter Debug.WriteLine

    [<Fact>]
    member  __. GetAllSPs() =
        conn.GetRoutines("dbo")
        |> Seq.map (sprintf "%A")
        |> Seq.iter Debug.WriteLine

    //[<Fact(Skip = "Until we gen SQL Azure with permissions")>]
//    [<Fact>]
//    member  __. GetParameters() =
//        conn.GetParameters( StoredProcedure("dbo", "Swap"))
//        |> Seq.map (sprintf "%A")
//        |> Seq.iter Debug.WriteLine

    