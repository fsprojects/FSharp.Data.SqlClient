module FSharp.Data.ProgrammabilityTest

open System.Data.SqlClient
open Xunit

[<Literal>] 
let connection = ConnectionStrings.AdventureWorksNamed

type AdventureWorks = SqlProgrammabilityProvider<connection>

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

[<Fact>]
let ConnectionObject() =
    let conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    let cmd = new GetLeadingZeros()
    let x = 42
    Assert.Equal( Some(sprintf "%08i" x), cmd.Execute(x))

type Address_GetAddressBySpatialLocation = AdventureWorks.Person.Address_GetAddressBySpatialLocation
open Microsoft.SqlServer.Types

[<Fact>]
let ``GEOMETRY and GEOGRAPHY sp params``() =
    use cmd = new Address_GetAddressBySpatialLocation()
    cmd.AsyncExecute(SqlGeography.Null) |> ignore
    
[<Fact>]
let routineCommandTypeTag() = 
    Assert.Equal<string>(ConnectionStrings.AdventureWorksNamed, GetContactInformation.ConnectionStringOrName)
