
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
    use cmd = new SqlCommand("select * from sys.types", conn)
    let track = SqlDependency(cmd)
    track.OnChange.Add(fun args ->
        printfn "Change: %A" args
    )