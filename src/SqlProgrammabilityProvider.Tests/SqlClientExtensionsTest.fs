module Test.SqlClientExtensionstest

open System.Data
open System.Data.SqlClient
open Xunit

open FSharp.Data

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
let AllTypes() =
    SqlClrTypes() 
    |> Seq.groupBy(fun t->t.SqlEngineTypeId)
    |> Seq.iter (printfn "%A")

[<Fact>]
let GetFullQualityColumnInfo() =
    conn.GetFullQualityColumnInfo("dbo.uspGetWhereUsedProductID") 
    |> Seq.iter (printfn "%A") 

    
[<Fact>]
let GetFunctionColumns() =
    conn.GetFunctionColumns("dbo.ufnGetContactInformation") 
    |> Seq.iter (printfn "%A") 

[<Fact>]
let GetFunctions() =
    conn.GetFunctions() 
    |> Seq.iter (printfn "%A") 

[<Fact>]
let GetAllSPs() =
    conn.GetProcedures()
    |> Seq.iter (printfn "%A")

[<Fact>]
let GetParameters() =
    conn.GetParameters("dbo.Swap", false)
    |> Seq.iter (printfn "%A")