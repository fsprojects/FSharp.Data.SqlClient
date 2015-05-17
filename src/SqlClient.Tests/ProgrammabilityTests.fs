module FSharp.Data.ProgrammabilityTest

open System
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

[<Fact>]
let localTransactionCtor() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    conn.Open()
    use tran = conn.BeginTransaction()
    let jamesKramerId = 42

    let businessEntityID, jobTitle, hireDate = 
        use cmd = new SqlCommandProvider<"
            SELECT 
	            BusinessEntityID
	            ,JobTitle
	            ,HireDate
            FROM 
                HumanResources.Employee 
            WHERE 
                BusinessEntityID = @id
            ", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples, SingleRow = true>(conn, tran)
        jamesKramerId |> cmd.Execute |> Option.get

    Assert.Equal<string>("Production Technician - WC60", jobTitle)
    
    let newJobTitle = "Uber " + jobTitle
    do
        //let get
        use updatedJobTitle = new AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo(conn, tran)
        let recordsAffrected = 
            updatedJobTitle.Execute(
                businessEntityID, 
                newJobTitle, 
                hireDate, 
                RateChangeDate = DateTime.Now, 
                Rate = 12M, 
                PayFrequency = 1uy, 
                CurrentFlag = true 
            )
        System.Diagnostics.Debug.WriteLine(recordsAffrected)
        //Assert.Equal(1, recordsAffrected)
    
    let updatedJobTitle = 
        use cmd = new AdventureWorks.dbo.ufnGetContactInformation(conn, tran)
        let result = cmd.Execute(PersonID = jamesKramerId) |> Seq.exactlyOne
        result.JobTitle.Value

    Assert.Equal<string>(newJobTitle, updatedJobTitle)
        
[<Fact>]
let localTransactionCreateAndSingleton() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    conn.Open()
    use tran = conn.BeginTransaction()
    let jamesKramerId = 42

    let businessEntityID, jobTitle, hireDate = 
        use cmd = SqlCommandProvider<"
            SELECT 
	            BusinessEntityID
	            ,JobTitle
	            ,HireDate
            FROM 
                HumanResources.Employee 
            WHERE 
                BusinessEntityID = @id
            ", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples, SingleRow = true>.Create(conn, tran)
        jamesKramerId |> cmd.Execute |> Option.get

    Assert.Equal<string>("Production Technician - WC60", jobTitle)
    
    let newJobTitle = "Uber " + jobTitle
    do
        //let get
        use updatedJobTitle = AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo.Create(conn, tran)
        let recordsAffrected = 
            updatedJobTitle.Execute(
                businessEntityID, 
                newJobTitle, 
                hireDate, 
                RateChangeDate = DateTime.Now, 
                Rate = 12M, 
                PayFrequency = 1uy, 
                CurrentFlag = true 
            )
        System.Diagnostics.Debug.WriteLine(recordsAffrected)
        //Assert.Equal(1, recordsAffrected)
    
    let updatedJobTitle = 
        use cmd = AdventureWorks.dbo.ufnGetContactInformation.Create(conn, tran)
        let result = cmd.Execute(PersonID = jamesKramerId) |> Seq.exactlyOne
        result.JobTitle.Value

    Assert.Equal<string>(newJobTitle, updatedJobTitle)