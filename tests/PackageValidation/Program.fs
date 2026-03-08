module PackageValidation

open System
open System.Configuration
open FSharp.Data

[<Literal>]
let ConnName = "name=AdventureWorks"

// ── SqlCommandProvider ──────────────────────────────────────────────────────
type QueryProducts =
    SqlCommandProvider<
        "SELECT TOP (@top) Name AS ProductName, SellStartDate
         FROM Production.Product
         WHERE SellStartDate > @SellStartDate",
        ConnName
     >

// ── SqlProgrammabilityProvider ──────────────────────────────────────────────
type AW = SqlProgrammabilityProvider<ConnName>

// ── SqlEnumProvider ─────────────────────────────────────────────────────────
type SalesReasons = SqlEnumProvider<"SELECT Name, SalesReasonID FROM Sales.SalesReason", ConnName>

[<EntryPoint>]
let main _argv =
    let connStr =
        ConfigurationManager.ConnectionStrings.["AdventureWorks"].ConnectionString

    printfn "=== Package Validation ==="
    printfn "Validating FSharp.Data.SqlClient NuGet package via PackageReference"
    printfn ""

    printfn "--- SqlCommandProvider ---"

    use cmd = new QueryProducts(connStr)
    let rows = cmd.Execute(top = 3L, SellStartDate = DateTime.Parse("2002-06-01"))

    for row in rows do
        printfn "  %-40s  %O" row.ProductName row.SellStartDate

    printfn ""
    printfn "--- SqlEnumProvider ---"
    printfn "  Sales-reason 'Price' id = %d" SalesReasons.Price

    printfn ""
    printfn "--- SqlProgrammabilityProvider ---"

    use cmd2 = new AW.dbo.ufnGetContactInformation (connStr)
    let contacts = cmd2.Execute(PersonID = 1)

    for row in contacts do
        printfn
            "  PersonID=%d  Name=%s  JobTitle=%s  Type=%s"
            row.PersonID
            row.FirstName.Value
            row.JobTitle.Value
            row.BusinessEntityType.Value

    printfn ""
    printfn "=== Package validation succeeded ==="
    0
