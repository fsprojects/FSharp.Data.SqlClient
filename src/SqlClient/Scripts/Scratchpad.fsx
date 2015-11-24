
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
let cmd = new SqlCommand("uspLogError", conn, CommandType = CommandType.StoredProcedure)
SqlCommandBuilder.DeriveParameters(cmd)
for p in cmd.Parameters do
    printfn "Name: %s, type: %A, direction: %A" p.ParameterName p.SqlDbType p.Direction