module FSharp.Data.ProgrammabilityTest

open System
open System.Data.SqlClient
open Xunit

type AdventureWorks = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksNamed>

type GetContactInformation = AdventureWorks.dbo.ufnGetContactInformation

[<Fact>]
let TableValuedFunction() =
    let cmd = new GetContactInformation()
    let person = cmd.Execute(PersonID = 1) |> Seq.exactlyOne
    let expected = GetContactInformation.Record(PersonID = 1, FirstName = Some "Ken", LastName = Some "Sánchez", JobTitle = Some "Chief Executive Officer", BusinessEntityType = Some "Employee")
    Assert.Equal(expected, person)

[<Fact>]
let ScalarValuedFunction() =
    let cmd = new AdventureWorks.dbo.ufnLeadingZeros()
    let x = 42
    Assert.Equal(Some(sprintf "%08i" x), cmd.Execute(x))
    //async execution
    Assert.Equal(Some(sprintf "%08i" x), cmd.AsyncExecute(x) |> Async.RunSynchronously)

[<Fact>]
let ConnectionObject() =
    let conn = new SqlConnection(ConnectionStrings.AdventureWorks)
    let cmd = new AdventureWorks.dbo.ufnLeadingZeros()
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
    use conn = new SqlConnection(ConnectionStrings.AdventureWorks)
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
        //Assert.Equal(1, recordsAffrected)
        ()
    
    let updatedJobTitle = 
        use cmd = new AdventureWorks.dbo.ufnGetContactInformation(conn, tran)
        let result = cmd.Execute(PersonID = jamesKramerId) |> Seq.exactlyOne
        result.JobTitle.Value

    Assert.Equal<string>(newJobTitle, updatedJobTitle)
        
[<Fact>]
let localTransactionCreateAndSingleton() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorks)
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
        //Assert.Equal(1, recordsAffrected)
        ()
    
    let updatedJobTitle = 
        use cmd = AdventureWorks.dbo.ufnGetContactInformation.Create(conn, tran)
        let result = cmd.ExecuteSingle(PersonID = jamesKramerId) 
        result.Value.JobTitle.Value

    Assert.Equal<string>(newJobTitle, updatedJobTitle)

[<Fact>]
let FunctionWithParamOfValueTypeWithNullDefault() = 
    Assert.Equal(
        AdventureWorks.dbo.ufnGetStock.Create().Execute(1),
        AdventureWorks.dbo.ufnGetStock2.Create().Execute(Some 1)
    )
    Assert.Equal(
        Some 83173,
        AdventureWorks.dbo.ufnGetStock2.Create().Execute()
    )

[<Fact>]
let SpWithParamOfRefTypeWithNullDefault() = 
    Assert.Equal(
        Some (Some (box "Empty")),
        AdventureWorks.dbo.Echo.Create().ExecuteSingle()
    )

    Assert.Equal(
        Some(Some (box 42)),
        AdventureWorks.dbo.Echo.Create().ExecuteSingle 42
    )

    Assert.Equal<string seq>(
        [| "<NULL>" |],
        AdventureWorks.dbo.EchoText.Create().Execute() |> Seq.toArray
    )

    let param = "Hello, world!"
    Assert.Equal<string seq>(
        [| param |],
        AdventureWorks.dbo.EchoText.Create().Execute param |> Seq.toArray
    )

type DboMyTableType = AdventureWorks.dbo.``User-Defined Table Types``.MyTableType 

[<Fact>]
let SpWithParamOfTvpWithNullableColumns() = 
    use cmd = new AdventureWorks.dbo.MyProc()
    let p = [
        DboMyTableType(myId = 1)
        DboMyTableType(myId = 2, myName = Some "donkey")
    ]
    let actual = [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    Assert.Equal<_ []>(
        [| 1, None; 2, Some "donkey" |],
        [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    )

type PersonMyTableType = AdventureWorks.Person.``User-Defined Table Types``.MyTableType 

[<Fact>]
let SpWithParamOfTvpWithNullableColumns2() = 
    use cmd = new AdventureWorks.Person.MyProc()
    let p = [
        PersonMyTableType(myId = 1)
        PersonMyTableType(myId = 2, myName = Some "donkey")
    ]
    let actual = [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    Assert.Equal<_ []>(
        [| 1, None; 2, Some "donkey" |],
        [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    )

[<Fact>]
let SpAndTVPinDiffSchema() = 
    use cmd = new AdventureWorks.Person.MyProc2()
    let p = [
        DboMyTableType(myId = 1)
        DboMyTableType(myId = 2, myName = Some "donkey")
    ]
    let actual = [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    Assert.Equal<_ []>(
        [| 1, None; 2, Some "donkey" |],
        [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    )

[<Fact>]
let OutParam() = 
    let cmd = new AdventureWorks.dbo.AddRef()
    let x, y = 12, -1
    let result = ref Int32.MinValue
    cmd.Execute(x, y, result) |> ignore
    Assert.Equal(x + y, !result)
    //tupled syntax
    let _, sum = cmd.Execute(x, y)
    Assert.Equal(x + y, sum)

[<Fact>]
let ResultSetAndOutParam() = 
    let cmd = new AdventureWorks.dbo.HowManyRows()
    let p = [
        DboMyTableType(myId = 1)
        DboMyTableType(myId = 2, myName = Some "donkey")
    ]
    let total = ref 0L
    let result = cmd.Execute(p, total) 
    Assert.Equal<_ list>(
        [ Some "donkey" ], 
        [ for row in result.Rows -> row.myName ]
    )
    Assert.Equal(2L, !total)
