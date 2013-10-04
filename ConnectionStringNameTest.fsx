printfn "load provider"

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

printfn "define type"
type QueryProductsAsTuples  = SqlCommand<queryProductsSql, connectionString, ConnectionStringName = "AdventureWorks2012">
printfn "create command"
let cmd = QueryProductsAsTuples(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
//cmd.Execute() |> Async.RunSynchronously |> Seq.iter (fun(productName, sellStartDate) -> printfn "Product name: %s. Sells start date %A" productName sellStartDate)

