#r "../../bin/Fsharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types.dll"
#r @"..\..\packages\FSharp.Configuration.0.5.3\lib\net40\FSharp.Configuration.dll"
//#load "ConnectionStrings.fs"
open System
open System.Data
open FSharp.Data


//[<Literal>] 
//let connectionString = ConnectionStrings.AdventureWorksLiteral
//let connectionString = ConnectionStrings.AdventureWorksAzure

//[<Literal>] 
//let prodConnectionString = ConnectionStrings.MasterDb

type AdventureWorks = SqlProgrammabilityProvider<"Data Source=.;Initial Catalog = AdventureWorks2014;Integrated Security=True">
type dbo = AdventureWorks.dbo

let swap = new dbo.Swap()
let output = ref 42
//let mutable outputBit = true
//let mutable outputStr = ""
let x = swap.Execute(12, output)
output