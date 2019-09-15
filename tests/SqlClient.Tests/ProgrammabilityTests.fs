module FSharp.Data.ProgrammabilityTest

open System
open System.Data.SqlClient
open Xunit

type AdventureWorks = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksNamed>

type AdventureWorksDataTables = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksLiteral, ResultType = ResultType.DataTable>
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
    let _ = new SqlConnection(ConnectionStrings.AdventureWorks)
    let cmd = new AdventureWorks.dbo.ufnLeadingZeros()
    let x = 42
    Assert.Equal( Some(sprintf "%08i" x), cmd.Execute(x))

type AdventureWorksFromDesignOnly = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksDesignOnly, ConfigFile = "appWithInclude.config">

[<Fact>]
let ``update table should work when names connection string from design time is not available at the runtime but is substituded with a literal``() = 
    use table = new AdventureWorksFromDesignOnly.Sales.Tables.SalesTerritory()        
    use conn = new SqlConnection(ConnectionStrings.AdventureWorks)
    conn.Open()
    use tran = conn.BeginTransaction()
    table.AddRow("Northwest3", "US", "North America")
    table.Update(conn, tran) |> ignore

[<Fact>]
let ``no bad error message when db is changed before table update ``() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorks)
    conn.Open()
    use tran = conn.BeginTransaction()
        
    use cmd = new SqlCommand("EXEC sp_rename 'Sales.SalesTerritory', 'SalesTerr';", conn, tran)

    cmd.ExecuteNonQuery() |> ignore

    use table = new AdventureWorks.Sales.Tables.SalesTerritory()
    table.AddRow("Northwest2", "US", "North America")
    
    Assert.Throws<SqlException>(fun _ ->  table.Update(conn, tran) |> ignore) |> ignore   
    
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
            SELECT BusinessEntityID, JobTitle, HireDate
            FROM HumanResources.Employee 
            WHERE BusinessEntityID = @id
            ", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples, SingleRow = true>(transaction = tran)
        jamesKramerId |> cmd.Execute |> Option.get

    Assert.Equal<string>("Production Technician - WC60", jobTitle)
    
    let newJobTitle = "Uber " + jobTitle
    do
        //let get
        use updatedJobTitle = new AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo(transaction = tran)
        let _ = 
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
        use cmd = new AdventureWorks.dbo.ufnGetContactInformation(transaction = tran)
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
            ", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples, SingleRow = true>.Create(transaction = tran)
        jamesKramerId |> cmd.Execute |> Option.get

    Assert.Equal<string>("Production Technician - WC60", jobTitle)
    
    let newJobTitle = "Uber " + jobTitle
    do
        //let get
        use updatedJobTitle = new AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo(transaction = tran)
        let _ = 
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
        use cmd = new AdventureWorks.dbo.ufnGetContactInformation(transaction = tran)
        let result = cmd.ExecuteSingle(PersonID = jamesKramerId) 
        result.Value.JobTitle.Value

    Assert.Equal<string>(newJobTitle, updatedJobTitle)

[<Fact>]
let FunctionWithParamOfValueTypeWithNullDefault() = 
    use cmd1 = new AdventureWorks.dbo.ufnGetStock()
    use cmd2 = new AdventureWorks.dbo.ufnGetStock2()
    Assert.Equal(cmd1.Execute(1), cmd2.Execute(Some 1))

    Assert.Equal(Some 83173, cmd2.Execute(None))

[<Fact>]
let SpWithParamOfRefTypeWithNullDefault() = 
    use echo = new AdventureWorks.dbo.Echo()
    Assert.Equal( Some (Some (box "Empty")), echo.ExecuteSingle(None))

    Assert.Equal( Some(Some (box 42)), echo.ExecuteSingle 42)

    use echoText = new AdventureWorks.dbo.EchoText()
    Assert.Equal<string[]>([| "<NULL>" |], echoText.Execute(null) |> Seq.toArray)

    let param = "Hello, world!"
    Assert.Equal<string[]>([| param |], echoText.Execute( param) |> Seq.toArray)

type DboMyTableType = AdventureWorks.dbo.``User-Defined Table Types``.MyTableType 

[<Fact>]
let SpWithParamOfTvpWithNullableColumns() = 
    use cmd = new AdventureWorks.dbo.MyProc()
    let p = [
        DboMyTableType(myId = 1)
        DboMyTableType(myId = 2, myName = Some "donkey")
    ]
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
    Assert.Equal<_ []>(
        [| 1, None; 2, Some "donkey" |],
        [| for x in cmd.Execute( p) -> x.myId, x.myName |]
    )

[<Fact>]
let OutParam() = 
    let cmd = new AdventureWorks.dbo.AddRef()
    let x, y = 12, -1
    let sum = ref Int32.MinValue
    cmd.Execute(x, y, sum) |> ignore
    Assert.Equal(x + y, !sum)
    //tupled syntax
    let _, sum2 = cmd.Execute(x, y)
    Assert.Equal(x + y, sum2)

[<Fact>]
let ResultSetAndOutParam() = 
    let cmd = new AdventureWorks.dbo.HowManyRows()
    let p = [
        DboMyTableType(myId = 1)
        DboMyTableType(myId = 2, myName = Some "donkey")
    ]
    let total = ref 0L
    let result = cmd.Execute(p, total) 
    Assert.Equal<_ list>([ Some "donkey" ], [ for x in result -> x.myName ] )
    Assert.Equal(2L, !total)

// Fix #340: 
//    The type provider cannot use Expr.Value(Activator.CreateInstance(t),t) for
//    non primitive types, like System.Guid.
//    It now use Expr.DefaultValue(t) for non primite types.
//    The PassGuid SP copies the input guid to the output parameter when the boolean
//    parameter is true, and let the out parameter uninitialized when false
[<Fact>]
let NonPrimitiveOutParam() =
    let guid = Guid.NewGuid()
    let cmd = new AdventureWorks.dbo.PassGuid()
    let _,result = cmd.Execute(guid, true)
    Assert.Equal(guid, result)

// Fix #340: 
//    When an output parameter has not been set, its value is DBNull which cannot
//    be unboxed. The fix sets the parameter to defaultOf<'t>.
//    The PassGuid SP copies the input guid to the output parameter when the boolean
//    parameter is true, and let the out parameter uninitialized when false
[<Fact>]
let NonPrimitiveNullOutParam() =
    let guid = Guid.NewGuid()
    let cmd = new AdventureWorks.dbo.PassGuid()
    let _,result = cmd.Execute(guid, false)
    Assert.Equal(Guid.Empty, result)

[<Fact>]
let PassingImageAsParamDoesntGetCut() = 
    use tran = new System.Transactions.TransactionScope() 
    use read = 
        new SqlCommandProvider<"
            SELECT TOP 1 ProductPhotoID, LargePhoto 
            FROM Production.ProductPhoto
            ORDER BY ProductPhotoID DESC
        ", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
    let existing = read.Execute().Value

    use write = new AdventureWorks.dbo.TestPhoto()
    let id = 200
    let inserted = write.ExecuteSingle(id, existing.LargePhoto.Value).Value
    Assert.Equal(id, inserted.ProductPhotoId)
    Assert.Equal(existing.LargePhoto.Value.Length, inserted.LargePhoto.Value.Length)
    Assert.Equal(existing.LargePhoto, inserted.LargePhoto)
    
[<Fact>]
let ``honors result type parameter: datatable`` () =
    let command = new AdventureWorksDataTables.Sales.GetUKSalesOrders(ConnectionStrings.AdventureWorksLiteral)
    let gbp = 1.0M<AdventureWorksDataTables.Sales.``Units of Measure``.GBP>
    let table : AdventureWorksDataTables.Sales.GetUKSalesOrders.Table = command.Execute(gbp)
    Assert.Equal<string>("Year", table.Columns.Year.ColumnName)
    
[<Fact>]    
let StaticCreateMethod() =
    let input1 = [AdventureWorks.Person.``User-Defined Table Types``.MyTableType(42, Some "BSL")]
    use newCmd = new AdventureWorks.Person.MyProc()
    let newResult = newCmd.Execute(input1)
    use factoryCmd = AdventureWorks.Person.MyProc.Create()
    let factoryResult = factoryCmd.Execute(input1)
    //Assert.Equal<Collections.Generic.IEnumerable<AdventureWorks.Person.MyProc.Record>>(newResult, factoryResult)
    Assert.Equal(Seq.length newResult, Seq.length factoryResult)
    (newResult, factoryResult) ||> Seq.iter2 (fun newR facR ->
        Assert.Equal(newR.myId, facR.myId)
        Assert.Equal(newR.myName, facR.myName))
    
    let input2 = [AdventureWorks.Person.``User-Defined Table Types``.MyTableType(420)]
    use newCmd = new AdventureWorks.Person.MyProc(ConnectionStrings.AdventureWorksLiteral, 58)
    let newResult = newCmd.Execute(input2)
    use factoryCmd = AdventureWorks.Person.MyProc.Create(ConnectionStrings.AdventureWorksLiteral, 58)
    let factoryResult = factoryCmd.Execute(input2)
    //Assert.Equal<Collections.Generic.IEnumerable<AdventureWorks.Person.MyProc.Record>>(newResult, factoryResult)
    Assert.Equal(Seq.length newResult, Seq.length factoryResult)
    (newResult, factoryResult) ||> Seq.iter2 (fun newR facR ->
        Assert.Equal(newR.myId, facR.myId)
        Assert.Equal(newR.myName, facR.myName))
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    do conn.Open()
    use newTran = conn.BeginTransaction()
    use newCmd = new AdventureWorks.Person.MyProc(conn, newTran)
    let newResult = newCmd.Execute(input1) |> Seq.toList
    do newTran.Dispose()
    use factoryTran = conn.BeginTransaction()
    use factoryCmd = AdventureWorks.Person.MyProc.Create(conn, factoryTran)
    let factoryResult = factoryCmd.Execute(input1) |> Seq.toList
    //Assert.Equal<Collections.Generic.IEnumerable<AdventureWorks.Person.MyProc.Record>>(newResult, factoryResult)
    Assert.Equal(List.length newResult, List.length factoryResult)
    (newResult, factoryResult) ||> List.iter2 (fun newR facR ->
        Assert.Equal(newR.myId, facR.myId)
        Assert.Equal(newR.myName, facR.myName))