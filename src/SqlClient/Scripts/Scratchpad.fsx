
#r "System.Transactions"
open System.Data.SqlClient
open System.Data

open System
open System.IO
open System.Data
open System.Data.SqlTypes

[<Literal>]
let connStr = "Data Source=.;Initial Catalog=SEN-QA-2015-12-10-11-13;Integrated Security=True"
let conn = new SqlConnection(connStr)
conn.Open()
let cmd = new SqlCommand("ThermalModel.GetFields", conn)
cmd.CommandType <- CommandType.StoredProcedure
let t = new DataTable()
do 
    use cursor = cmd.ExecuteReader(CommandBehavior.SingleRow)
    t.Load cursor

t.Rows.Count
