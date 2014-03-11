module WebApi.DataAccess

open FSharp.Data
[<Literal>]

let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

[<Literal>]
let AdventureWorks2012 = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type QueryProducts = SqlCommand<queryProductsSql, AdventureWorks2012>

//OR connection string by name and command text from file
//type QueryProducts = SqlCommand<"T-SQL\Products.sql", "name=AdventureWorks2012", ResultType = ResultType.Records, ConfigFile = "user.config">
