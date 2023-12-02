module FSharp.Data.SqlClient.Tests.UnitsOfMeasure

open Xunit
open FSharp.Data
open FSharp.Data.SqlClient
open FSharp.Data.SqlClient.Tests
type DB = FSharp.Data.SqlClient.Tests.ProgrammabilityTest.AdventureWorks

type UOM = DB.Sales.``Units of Measure``

[<Fact>]
let SingleOutput() =
    use cmd = DB.CreateCommand<"SELECT SUM(TotalDue) FROM Sales.UnitedKingdomOrders", SingleRow = true>()
    Assert.Equal<_>(Some( Some(8570333.1218M<UOM.GBP>)), cmd.Execute())

[<Fact>]
let WithParam() =
    use cmd = DB.CreateCommand<"
        DECLARE @minTemp AS Sales.[<GBP>] = @min
        SELECT 
	        Total = SUM(x.TotalDue)
	        ,[Year] = DATEPART(year, y.OrderDate)
        FROM Sales.UnitedKingdomOrders x
	        JOIN Sales.SalesOrderHeader y on x.SalesOrderID = y.SalesOrderID
        GROUP BY DATEPART(year, y.OrderDate)
        HAVING SUM(x.TotalDue) > @minTemp
        ORDER BY 1
    ", TypeName = "GetUKSales">()

    let actual = cmd.Execute(2000000M<UOM.GBP>) |> Seq.toArray
    let expected = [|
        DB.Commands.GetUKSales.Record(Total = Some 2772402.4754M<UOM.GBP>, Year = Some 2008)
        DB.Commands.GetUKSales.Record(Total = Some 3873351.7965M<UOM.GBP>, Year = Some 2007)
    |]

    Assert.Equal<_[]>(expected, actual)

[<Fact>]
let FunctionInQuery() =
    use cmd = DB.CreateCommand<"SELECT * FROM Sales.GetUKSalesOrders(@min) ORDER BY 1", ResultType.Tuples>()

    let actual = cmd.Execute(2000000M<UOM.GBP>) |> Seq.toArray
    let expected = [|
        Some 2772402.4754M<UOM.GBP>, Some 2008
        Some 3873351.7965M<UOM.GBP>, Some 2007
    |]

    Assert.Equal<_[]>(expected, actual)

[<Fact>]
let FunctionDirect() =
    use cmd = new DB.Sales.GetUKSalesOrders()
    let actual = cmd.Execute(2000000M<UOM.GBP>) |> Seq.toArray |> Array.sortBy (fun x -> x.Total)
    let expected = [|
        DB.Sales.GetUKSalesOrders.Record(Total = Some 2772402.4754M<UOM.GBP>, Year = Some 2008)
        DB.Sales.GetUKSalesOrders.Record(Total = Some 3873351.7965M<UOM.GBP>, Year = Some 2007)
    |]

    Assert.Equal<_[]>(expected, actual)

[<Fact>]
let Datatable() =
    use cmd = DB.CreateCommand<"SELECT * FROM Sales.GetUKSalesOrders(@min) ORDER BY 1", ResultType.DataTable>()

    let actual = [| 
        for x in cmd.Execute(2000000M<UOM.GBP>).Rows do 
            let total: decimal<UOM.GBP> option = x.Total
            yield total, x.Year
    |]
    let expected = [|
        Some 2772402.4754M<UOM.GBP>, Some 2008
        Some 3873351.7965M<UOM.GBP>, Some 2007
    |]

    Assert.Equal<_[]>(expected, actual)

