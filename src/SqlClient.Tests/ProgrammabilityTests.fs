module FSharp.Data.ProgrammabilityTest

open Xunit
open FsUnit.Xunit

[<Literal>] 
//let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
let connectionString = @"name = AdventureWorks2012"

type AdventureWorks = SqlProgrammabilityProvider<connectionString>
type Functions = AdventureWorks.Functions
type StoredProcedures = AdventureWorks.``Stored Procedures``

type GetContactInformation = Functions.``dbo.ufnGetContactInformation``

[<Fact>]
let TableValuedFunction() =
    let cmd = new GetContactInformation()
    let person = cmd.Execute(PersonID = 1) |> Seq.exactlyOne
    let expected = GetContactInformation.Record(personID = 1, firstName = Some "Ken", lastName = Some "Sánchez", jobTitle = Some "Chief Executive Officer", businessEntityType = Some "Employee")
    Assert.Equal(expected, person)

type GetLeadingZeros = Functions.``dbo.ufnLeadingZeros``

[<Fact>]
let ScalarValuedFunction() =
    let cmd = new GetLeadingZeros()
    let x = 42
    Assert.Equal(Some(sprintf "%08i" x), cmd.Execute(x))

