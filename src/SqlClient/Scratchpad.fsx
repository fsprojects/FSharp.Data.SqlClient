
#r "System.Transactions"

open System
open System.IO
open System.Data
open System.Data.SqlClient
open System.Data.SqlTypes

[<Literal>]
let connStr = "Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"
let conn = new SqlConnection(connStr)
conn.Open()

let cmd = new SqlCommand("select * from sys.databases", conn)
let reader = cmd.ExecuteReader() 
reader |> Seq.cast<obj> |> Seq.map (fun x -> x.GetType().FullName) |> Seq.head |> printfn "%A"
