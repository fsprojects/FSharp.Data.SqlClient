module ConsoleSample

open System
open System.Configuration
open FSharp.Data

[<Literal>]
let ConnName = "name=AdventureWorks"

// ── SqlCommandProvider ──────────────────────────────────────────────────────
// Typed query: top N products with SellStartDate after the given date.
type QueryProducts =
    SqlCommandProvider<
        "SELECT TOP (@top) Name AS ProductName, SellStartDate
         FROM Production.Product
         WHERE SellStartDate > @SellStartDate",
        ConnName
     >

// ── SqlProgrammabilityProvider ──────────────────────────────────────────────
// Exposes the whole AdventureWorks schema (stored procedures, TVFs, UDTs…).
type AW = SqlProgrammabilityProvider<ConnName>

// ── SqlEnumProvider ─────────────────────────────────────────────────────────
// Maps a look-up table to a discriminated-union-like set of constants.
type SalesReasons = SqlEnumProvider<"SELECT Name, SalesReasonID FROM Sales.SalesReason", ConnName>

[<EntryPoint>]
let main _argv =
    let connStr =
        ConfigurationManager.ConnectionStrings.["AdventureWorks"].ConnectionString

    printfn "=== SqlCommandProvider demo ==="

    use cmd = new QueryProducts(connStr)
    let rows = cmd.Execute(top = 5L, SellStartDate = DateTime.Parse("2002-06-01"))

    for row in rows do
        printfn "  %-40s  %O" row.ProductName row.SellStartDate

    printfn ""
    printfn "=== SqlEnumProvider demo ==="
    printfn "  Sales-reason 'Price' id = %d" SalesReasons.Price
    printfn "  Sales-reason 'On Promotion' id = %d" SalesReasons.``On Promotion``

    printfn ""
    printfn "=== SqlProgrammabilityProvider demo ==="

    use cmd2 = new AW.dbo.ufnGetContactInformation (connStr)
    let contacts = cmd2.Execute(PersonID = 1)

    for row in contacts do
        printfn
            "  PersonID=%d  Name=%s  JobTitle=%s  Type=%s"
            row.PersonID
            row.FirstName.Value
            row.JobTitle.Value
            row.BusinessEntityType.Value

    0
