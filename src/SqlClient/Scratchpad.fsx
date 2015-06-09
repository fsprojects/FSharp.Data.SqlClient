
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

let cmd = new SqlCommand("select * from HumanResources.Shift where StartTime > @startTime", conn)
cmd.Parameters.AddWithValue("@startTime", TimeSpan.FromHours 12.)
let reader = cmd.ExecuteReader()
let dataTable = new DataTable()
dataTable.Load reader 
let row = dataTable.NewRow()
row.["Name"] <- "Test"
row.["StartTime"] <- TimeSpan.FromHours 1.
row.["EndTime"] <- TimeSpan.FromHours 5.
row.["ModifiedDate"] <- DateTime.Now

dataTable.Rows.Add row

//reader |> Seq.cast<obj> |> Seq.map (fun x -> x.GetType().FullName) |> Seq.head |> printfn "%A"

let dataAdapter = new SqlDataAdapter(cmd)

let commandBuilder = new SqlCommandBuilder(dataAdapter)
dataAdapter.InsertCommand <- commandBuilder.GetInsertCommand()
conn.Close()
dataAdapter.Update(dataTable)
conn.State
let clone = cmd.Clone()
clone.Connection = cmd.Connection
cmd.Parameters.[0].Value
clone.Parameters.[0].Value

cmd.UpdatedRowSource

[ for x in dataAdapter.InsertCommand.Parameters -> x.ParameterName, x.SourceColumn ]