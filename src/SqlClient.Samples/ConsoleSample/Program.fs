module ConsoleSample

open System
open FSharp.Data

[<Literal>]
let ConnStr =
    "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True;TrustServerCertificate=true"

// ── SqlCommandProvider ──────────────────────────────────────────────────────
// Typed query: top N products with SellStartDate after the given date.
type QueryProducts =
    SqlCommandProvider<
        "SELECT TOP (@top) Name AS ProductName, SellStartDate
         FROM Production.Product
         WHERE SellStartDate > @SellStartDate",
        ConnStr
     >

// ── SqlProgrammabilityProvider ──────────────────────────────────────────────
// Exposes the whole AdventureWorks schema (stored procedures, TVFs, UDTs…).
type AW = SqlProgrammabilityProvider<ConnStr>

// ── SqlEnumProvider ─────────────────────────────────────────────────────────
// Maps a look-up table to a discriminated-union-like set of constants.
type SalesReasons = SqlEnumProvider<"SELECT Name, SalesReasonID FROM Sales.SalesReason", ConnStr>

[<EntryPoint>]
let main _argv =
    printfn "=== SqlCommandProvider demo ==="

    use cmd = new QueryProducts()
    let rows = cmd.Execute(top = 5L, SellStartDate = DateTime.Parse("2002-06-01"))

    for row in rows do
        printfn "  %-40s  %O" row.ProductName row.SellStartDate

    printfn ""
    printfn "=== SqlEnumProvider demo ==="
    printfn "  Sales-reason 'Price' id = %d" SalesReasons.Price
    printfn "  Sales-reason 'On Promotion' id = %d" SalesReasons.``On Promotion``

    printfn ""
    printfn "=== SqlProgrammabilityProvider demo ==="
    use aw = new AW(ConnStr)
    let emp = aw.HumanResources.uspGetEmployeeManagers (BusinessEntityID = 2)

    for row in emp do
        printfn
            "  Level %d – %s %s (reports to %s %s)"
            row.RecursionLevel
            row.FirstName
            row.LastName
            row.ManagerFirstName
            row.ManagerLastName

    0
