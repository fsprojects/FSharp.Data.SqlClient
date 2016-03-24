#r "../../../bin/Fsharp.Data.SqlClient.dll"
#r @"Microsoft.SqlServer.Types.dll"
open System
open System.Data
open FSharp.Data
open System.Data.SqlClient

[<Literal>] 
let connectionString = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type DB = SqlProgrammabilityProvider<connectionString>
type dbo = DB.dbo

//let x = DB.GetValue(42.)
//let xx = x * 12.<DB.bbl>

let connection = new SqlConnection(connectionString)
connection.Open()

let getTopSalespeople = 
    DB.CreateCommand<"
        SELECT TOP(@topN) FirstName, LastName, SalesYTD 
        FROM Sales.vSalesPerson
        WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
        ORDER BY SalesYTD
    ">(commandTimeout = 60)

getTopSalespeople.Execute(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) |> printfn "%A"

let get42AndTime = DB.CreateCommand<"SELECT 42, GETDATE()", ResultType.Tuples, SingleRow = true>(SqlClient.Connection.Instance connection)
get42AndTime.AsyncExecute() |> Async.RunSynchronously |> printfn "%A"

let myPerson = DB.CreateCommand<"exec Person.myProc @x", ResultType.Tuples, SingleRow = true, TypeName = "MyProc">()
type MyTableType = DB.Person.``User-Defined Table Types``.MyTableType
myPerson.Execute [ MyTableType(myId = 1, myName = Some "monkey"); MyTableType(myId = 2, myName = Some "donkey") ] 

open Microsoft.SqlServer.Types
open System.Data.SqlTypes

let getEmployeeByLevel = DB.CreateCommand<"
    SELECT OrganizationNode 
    FROM HumanResources.Employee 
    WHERE OrganizationNode = @OrganizationNode", SingleRow = true>()

let p = SqlHierarchyId.Parse(SqlString("/1/1/"))

getEmployeeByLevel.Execute( p)|> printfn "%A"
