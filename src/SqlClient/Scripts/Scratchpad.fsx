
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
let cmd = new SqlCommand("select * from dbo.TableHavingColumnNamesWithSpaces", conn)
let adapter = new SqlDataAdapter(cmd)
let t = adapter.FillSchema(new DataTable(), SchemaType.Source)
[ for c in t.Columns -> c.ColumnName, c.DataType.FullName, c.AllowDBNull ]

let r = t.NewRow()
r.["ID"] <- DBNull.Value
t.Rows.Add r
adapter.Update(t)
