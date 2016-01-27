
#r "System.Transactions"
open System.Data.SqlClient
open System.Data

open System
open System.IO
open System.Data
open System.Data.SqlTypes

[<Literal>]
let connStr = "Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"
let conn = new SqlConnection(connStr)
conn.Open()
let cmd = new SqlCommand("SELECT 42", conn)
let t = new DataTable()
do 
    use cursor = cmd.ExecuteReader(CommandBehavior.SingleRow)
    t.Load cursor

t.Rows.Count
