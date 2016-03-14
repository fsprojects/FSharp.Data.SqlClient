(*** hide ***)
#r @"..\..\bin\FSharp.Data.SqlClient.dll"
#r @"..\..\packages\xunit.1.9.2\lib\net20\xunit.dll"
#r "System.Transactions"
open FSharp.Data
open System

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

(**

Unit-testing
===================

Often there is a need to test business or presentation logic independently of database. 
This can be archived via [Repository](http://martinfowler.com/eaaCatalog/repository.html) pattern. 

*)

//Command types definitions
type GetEmployeesByLevel = 
    SqlCommandProvider<"
        SELECT P.FirstName, P.LastName, E.JobTitle
        FROM HumanResources.Employee AS E
	        JOIN Person.Person AS P ON E.BusinessEntityID = P.BusinessEntityID
        WHERE OrganizationLevel = @orgLevel
    ", connectionString>

type GetSalesChampion = SqlCommandProvider<"
    SELECT TOP 1 FirstName, LastName
    FROM Sales.vSalesPerson
    WHERE CountryRegionName = @countryRegionName
    ORDER BY SalesYTD DESC
    " , connectionString, SingleRow = true>

//Repository inteface
type IRepository = 
    abstract GetEmployeesByLevel: int16 -> list<GetEmployeesByLevel.Record>
    abstract GetSalesChampion: country: string -> option<GetSalesChampion.Record>

//Production implementation
type Repository(?connectionString: string) = 
    interface IRepository with 
        member __.GetEmployeesByLevel(orgLevel) = 
            use cmd = new GetEmployeesByLevel()
            cmd.Execute(orgLevel) |> Seq.toList

        member __.GetSalesChampion( region) = 
            use cmd = new GetSalesChampion()
            cmd.Execute(region) 
        
//logic to test
let whoReportsToCEO(db: IRepository) = 
    [ for x in db.GetEmployeesByLevel(1s) -> sprintf "%s %s" x.FirstName x.LastName, x.JobTitle ]

let bestSalesRepInCanada(db: IRepository) = 
    db.GetSalesChampion("Canada")

//unit tests suite
module MyTests = 
    
    //mock the real repo
    let mockRepository = {
        new IRepository with 
            member __.GetEmployeesByLevel(orgLevel) = 
                if orgLevel = 1s 
                then 
                    [ 
                        //Generated record types have single constructor that include all properties
                        //It exists to support unit-testing.
                        GetEmployeesByLevel.Record("David", "Bradley", JobTitle = "Marketing Manager") 
                    ]
                else []

            member __.GetSalesChampion( region) = 
                use cmd = new GetSalesChampion()
                cmd.Execute(region) 
    }

    //unit test
    let ``who reports to CEO``() = 
        let expected = [ "David Bradley", "Marketing Manager" ]
        assert (whoReportsToCEO mockRepository = expected)        
        //replace assert invocation above with invocation to your favorite unit testing framework 
        //fro example Xunit.NET: Assert.Equal<_ list>(expected, actual)

    //unit test
    let ``best sales rep in Canada``() = 
        let expected = Some( GetSalesChampion.Record("José", "Saraiva"))
        assert (bestSalesRepInCanada mockRepository = expected)        



