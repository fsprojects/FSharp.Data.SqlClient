
#r "bin/Debug/SqlCommandTypeProvider.dll"

open FSharp.NYC.Tutorial

[<Literal>]
//let connectionString="Data Source=mitekm-pc2;Initial Catalog=AdventureWorks2012;Integrated Security=True"
//let connectionString="Server=tcp:ybxsjdodsy.database.windows.net,1433;Database=AdventureWorks2012;User ID=stewie@ybxsjdodsy;Password=Leningrad1;Trusted_Connection=False;Encrypt=True;Connection Timeout=30;"
let connectionString="Server=tcp:lhwp7zue01.database.windows.net,1433;Database=AdventureWorks2012;User ID=jackofshadows@lhwp7zue01;Password=Leningrad1;Trusted_Connection=False;Encrypt=True;Connection Timeout=30;"

[<Literal>]
let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

type QueryProductsAsTuples = SqlCommand<queryProductsSql, connectionString>
let cmd = QueryProductsAsTuples(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
cmd.Execute() |> Async.RunSynchronously |> Seq.iter (fun(productName, sellStartDate) -> printfn "Product name: %s. Sells start date %A" productName sellStartDate)

type QueryProducts = SqlCommand<queryProductsSql, connectionString, ResultSetType = ResultSetType.DTOs>
let cmd1 = QueryProducts(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
cmd1.Execute() |> Async.RunSynchronously |> Seq.iter (fun x -> printfn "Product name: %s. Sells start date %A" x.ProductName x.SellStartDate)

type QueryProductDataTable = SqlCommand<queryProductsSql, connectionString, ResultSetType = ResultSetType.DataTable>
let cmd15 = QueryProductDataTable(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let xs = cmd15.Execute() |> Async.RunSynchronously 
xs |> Seq.map (fun row -> printfn "Product name: %s. Sells start date %O" row.ProductName row.SellStartDate)

type QueryPersonInfoSingletone = SqlCommand<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, ResultSetType = ResultSetType.DTOs, SingleRow=true>
let query = new QueryPersonInfoSingletone(PersonId = 2)
let x = query.Execute() |> Async.RunSynchronously 
printfn "Person info: Id - %i, FirstName - %s, LastName - %s, JobTitle - %s, BusinessEntityType - %s" x.PersonID x.FirstName x.LastName x.JobTitle x.BusinessEntityType

type UpdateEmplInfoCommand = SqlCommand<"EXEC HumanResources.uspUpdateEmployeePersonalInfo @BusinessEntityID, @NationalIDNumber, @BirthDate, @MaritalStatus, @Gender", connectionString>
let cmd2 = new UpdateEmplInfoCommand(BusinessEntityID = 2, NationalIDNumber = "245797967", BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F")
cmd2.Execute() |> Async.RunSynchronously

type GetServerTime = SqlCommand<"IF @IsUtc = CAST(1 AS BIT) SELECT GETUTCDATE() ELSE SELECT GETDATE()", connectionString, SingleRow=true>
let getSrvTime = new GetServerTime(IsUtc = true)
getSrvTime.Execute() |> Async.RunSynchronously |> printfn "%A"
getSrvTime.IsUtc <- false
getSrvTime.Execute() |> Async.RunSynchronously |> printfn "%A"
