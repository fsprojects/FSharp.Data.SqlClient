#nowarn "211"
#r @"..\..\bin\FSharp.Data.SqlClient.dll"
#r "System.Transactions"

open FSharp.Data

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"
