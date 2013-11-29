module FSharp.Data.SqlClient.Test.DataAccess

open FSharp.Data.Experimental

[<Literal>]
let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

type QueryProductsAsTuples = SqlCommand<queryProductsSql, ConnectionStringName="AdventureWorks2012", ConfigFile="web.config">

