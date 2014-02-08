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
    UDTTs() |> Seq.iter (fun x -> printfn "%A" x.UdttName)
