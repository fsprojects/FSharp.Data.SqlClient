module FSharp.Data.ProgrammabilityTest

open Xunit
open FsUnit.Xunit

[<Literal>] 
//let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
let connectionString = @"name = AdventureWorks2012"

type AdventureWorks = SqlProgrammabilityProvider<connectionString>

type GetContactInformation = AdventureWorks.dbo.ufnGetContactInformation

[<Fact>]
let TableValuedFunction() =
    let cmd = new GetContactInformation()
    let person = cmd.Execute(PersonID = 1) |> Seq.exactlyOne
    let expected = GetContactInformation.Record(PersonID = 1, FirstName = Some "Ken", LastName = Some "Sánchez", JobTitle = Some "Chief Executive Officer", BusinessEntityType = Some "Employee")
    Assert.Equal(expected, person)

type GetLeadingZeros = AdventureWorks.dbo.ufnLeadingZeros

[<Fact>]
let ScalarValuedFunction() =
    let cmd = new GetLeadingZeros()
    let x = 42
    Assert.Equal(Some(sprintf "%08i" x), cmd.Execute(x))
    //async execution
    Assert.Equal(Some(sprintf "%08i" x), cmd.AsyncExecute(x) |> Async.RunSynchronously)
    
