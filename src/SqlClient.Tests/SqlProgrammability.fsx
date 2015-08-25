#r "../../bin/Fsharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types.dll"
#load "ConnectionStrings.fs"
open System
open System.Data
open FSharp.Data


[<Literal>] 
let connectionString = ConnectionStrings.AdventureWorksLiteral
//let connectionString = ConnectionStrings.AdventureWorksAzure

[<Literal>] 
let prodConnectionString = ConnectionStrings.MasterDb

type AdventureWorks = SqlProgrammabilityProvider<connectionString>
type dbo = AdventureWorks.dbo
