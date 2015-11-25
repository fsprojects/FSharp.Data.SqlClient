
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
let cmd = new SqlCommand("dbo.AddRef", conn, CommandType = CommandType.StoredProcedure)

SqlCommandBuilder.DeriveParameters(cmd)
for p in cmd.Parameters do
    printfn "Name: %s, type: %A, direction: %A" p.ParameterName p.SqlDbType p.Direction

cmd.Parameters.["@x"].Value <- 12
cmd.Parameters.["@y"].Value  <- 3
cmd.Parameters.["@sum"].Value <- DBNull.Value
cmd.ExecuteNonQuery() 

cmd.Parameters.["@sum"].Value
cmd.Parameters.["@RETURN_VALUE"].Value

