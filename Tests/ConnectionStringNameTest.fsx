printfn "load provider"

#r "../bin/Debug/SqlCommandTypeProvider.dll"

open FSharp.NYC.Tutorial

[<Literal>]
let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

printfn "define type"
type QueryProductsAsTuples  = SqlCommand<queryProductsSql, ConnectionStringName = "AdventureWorks2012">
printfn "create command"
let cmd = QueryProductsAsTuples(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
//cmd.Execute() |> Async.RunSynchronously |> Seq.iter (fun(productName, sellStartDate) -> printfn "Product name: %s. Sells start date %A" productName sellStartDate)

