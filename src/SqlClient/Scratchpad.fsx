
#r "System.Transactions"

open System
open System.IO
open System.Data
open System.Data.SqlClient
open System.Data.SqlTypes

[<Literal>]
let connStr = "Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"
do 
    use conn = new SqlConnection(connStr)
    conn.Open()
    use tran = conn.BeginTransaction()
    let cmd = new SqlCommand()
    cmd.Connection <- conn
    cmd.Transaction <- tran
    cmd.CommandText <- 
        //"INSERT INTO [HumanResources].[Shift] ([Name], [StartTime], [EndTime]) VALUES (@p1, @p2, @p3)"
        "INSERT INTO [HumanResources].[Shift] ([Name], [StartTime], [EndTime], [ModifiedDate]) VALUES (@p1, @p2, @p3, @p4)"
    cmd.Parameters.AddWithValue("@p1", "French coffee break") |> ignore
    cmd.Parameters.AddWithValue("@p2", TimeSpan.FromHours 10.) |> ignore
    cmd.Parameters.AddWithValue("@p3", TimeSpan.FromHours 12.) |> ignore
    cmd.Parameters.AddRange [|
        SqlParameter("@p4", SqlDbType.DateTime2, IsNullable = true, Value = DBNull.Value)
    |]
    //cmd.Parameters.AddWithValue("@p4", DBNull.Value)
    cmd.ExecuteNonQuery() |> printfn "Records affected: %i"


let table = new DataTable()
let col = new DataColumn("ModifiedDate", typeof<DateTime>)
col.DefaultValue.GetType().FullName
col.AllowDBNull
table.Columns.Add col
