module FSharp.Data.SqlClientExtensionsTest

open System.Data
open System.Data.SqlClient
open Xunit
open FsUnit.Xunit

open FSharp.Data.SqlClient

[<Literal>]
let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
let conn = new SqlConnection(connectionString)
do 
    conn.Open()
    conn.LoadDataTypesMap()


[<Fact>]
let TestUDTTs() =
    UDTTs() 
    |> Seq.collect (fun x -> x.TvpColumns)
    |> Seq.iter (printfn "%A")    

[<Fact>]
let AllTypes() =
    SqlClrTypes() 
    |> Seq.groupBy(fun t->t.SqlEngineTypeId)
    |> Seq.iter (printfn "%A")

[<Fact>]
let GetFullQualityColumnInfo() =
    conn.GetFullQualityColumnInfo("dbo.uspGetWhereUsedProductID") 
    |> Seq.iter (printfn "%A") 

[<Fact>]
let GetAllSPs() =
    conn.GetProcedures()
    |> Seq.iter (printfn "%A")

[<Fact>]
let GetParameters() =
    conn.GetParameters(Map.empty, "dbo.Swap")
    //|> Seq.iter (printfn "%A")
    |> ignore

[<Fact>]
let ``Parse default value``() =
    parseDefaultValue("0", typeof<bool>) |> should equal (box false)
    parseDefaultValue("1", typeof<bool>) |> should equal (box true)
    parseDefaultValue("1", typeof<int8>) |> should equal (box 1y)