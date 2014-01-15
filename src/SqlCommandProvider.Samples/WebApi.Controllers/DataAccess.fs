module WebApi.DataAccess

open FSharp.Data.Experimental

[<Literal>]
let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

type QueryProductsAsTuples = SqlCommand<queryProductsSql, ConnectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True">

