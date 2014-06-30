module ProgrammabilityTest


open System
open System.Data
open FSharp.Data
open Microsoft.SqlServer.Types
open Xunit
open FsUnit.Xunit

[<Literal>] 
let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
//let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type Test = SqlProgrammabilityProvider<connectionString>

[<Fact>]
let TestFunctionCall() =
    Test().Functions.``dbo.ufnGetContactInformation``.AsyncExecute(123) |> Async.RunSynchronously |> Seq.iter (printfn "%A")