module Test.SqlClientExtensionstest

open System.Data
open System.Data.SqlClient
open Xunit

open FSharp.Data.Experimental.Runtime

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
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
let TestAssemblyTypes() =
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
    |> Seq.iter (fun x -> printfn "%A" x)