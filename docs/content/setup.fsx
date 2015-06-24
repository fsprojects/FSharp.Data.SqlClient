#nowarn "211"
#r @"..\..\src\SqlClient\bin\Debug\FSharp.Data.SqlClient.dll"
#r "System.Transactions"

open FSharp.Data

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"
