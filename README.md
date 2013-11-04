FSharp.Data.SqlCommandTypeProvider
==============================

## Features

* Typed access to the result of running a query. 
* Typed access to @parameters needed for running a query
* Fields that can be NULL translate to the F# Option type, forcing you to deal with the issue of null values directly.
* Sql is invalid -> Compiler error! 
* Results as tuples, records, or DataTable
* Sql can be inline, or in an external file
* Sync/Async execution

This project IS NOT a replacement for either SqlDataConnection or SqlEntityConnection type providers.

## Limitations

* Requires SQL Server 2012 or SQL Azure Database at compile-time
* Does not work with queries that use temporary tables 
* Works in F# only
* Parameters in a query may only be used once. 
   
    You can work around this by declaring a local variable in Sql, and assigning the @param to that local variable: 

    ```SQL
    DECLARE @input int
    SET @input = @param
    SELECT *
    FROM sys.indexes
    WHERE @input = 1 or @input = 2
    ```

## Code samples

Extra type annotations are for demo purposes only

```ocaml
#r "../SqlCommandTypeProvider/bin/Debug/SqlCommandTypeProvider.dll"

open FSharp.Data.SqlClient
open System.Data
open System

[<Literal>]
let connectionString="Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"
```

Command text

```ocaml
[<Literal>]
let queryProductsSql = " 
SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"
```

#### Tuples (default)

```ocaml
type QueryProductsAsTuples = SqlCommand<queryProductsSql, connectionString>
let cmd = QueryProductsAsTuples(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let result : Async<(string * DateTime) seq> = cmd.AsyncExecute()
result 
    |> Async.RunSynchronously 
    |> Seq.iter (fun(productName, sellStartDate) -> printfn "Product name: %s. Sells start date %A" productName sellStartDate)
```

#### Custom record types

```ocaml
type QueryProducts = 
    SqlCommand<queryProductsSql, connectionString, ResultType = ResultType.Records>
let cmd1 = QueryProducts(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let result1 : Async<QueryProducts.Record seq> = cmd1.AsyncExecute()
result1 
    |> Async.RunSynchronously 
    |> Seq.iter (fun x -> printfn "Product name: %s. Sells start date %A" x.ProductName x.SellStartDate)
```

#### DataTable for data binding scenarios and update

```ocaml
type QueryProductDataTable = 
    SqlCommand<queryProductsSql, connectionString, ResultType = ResultType.DataTable>
let cmd2 = QueryProductDataTable(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let result2 : Async<DataTable<QueryProductDataTable.Row>> = cmd2.AsyncExecute() 
result2 
    |> Async.RunSynchronously 
    |> Seq.map (fun row -> printfn "Product name: %s. Sells start date %O" row.ProductName row.SellStartDate)
```

#### Single row hint

```ocaml
type QueryPersonInfoSingletone = 
    SqlCommand<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, ResultType = ResultType.Records, SingleRow=true>
let cmd3 = new QueryPersonInfoSingletone(PersonId = 2)
let result3 : Async<QueryPersonInfoSingletone.Record> = cmd3.AsyncExecute() 
result3 
    |> Async.RunSynchronously 
    |> fun x -> printfn "Person info: Id - %i, FirstName - %s, LastName - %s, JobTitle - %s, BusinessEntityType - %s" x.PersonID x.FirstName x.LastName x.JobTitle x.BusinessEntityType
```

#### Non-query

```ocaml
type UpdateEmplInfoCommand = 
    SqlCommand<"EXEC HumanResources.uspUpdateEmployeePersonalInfo @BusinessEntityID, @NationalIDNumber, @BirthDate, @MaritalStatus, @Gender", connectionString>
let cmd4 = new UpdateEmplInfoCommand(BusinessEntityID = 2, NationalIDNumber = "245797967", BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "M")
let result4 : Async<int> = cmd4.AsyncExecute() 
let rowsAffected = result4 |> Async.RunSynchronously 
```

#### Single value

```ocaml
type GetServerTime = 
    SqlCommand<"IF @IsUtc = CAST(1 AS BIT) SELECT GETUTCDATE() ELSE SELECT GETDATE()", connectionString, SingleRow=true>
let getSrvTime = new GetServerTime(IsUtc = true)
let result5 : Async<DateTime> = getSrvTime.AsyncExecute()
result5 |> Async.RunSynchronously |> printfn "%A"
getSrvTime.IsUtc <- false
//Execute again synchronously
getSrvTime.Execute() |> printfn "%A"
```

#### Stored procedure by name only

```ocaml
type UpdateEmplInfoCommandSp = 
    SqlCommand<"HumanResources.uspUpdateEmployeePersonalInfo", connectionString, CommandType = CommandType.StoredProcedure>
let cmdSp = new UpdateEmplInfoCommandSp(BusinessEntityID = 2, NationalIDNumber = "245797967", BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F")
cmdSp.AsyncExecute() |> Async.RunSynchronously
cmdSp.SpReturnValue
```

#### Table-valued parameters

When using TVPs, the Sql command needs to be calling a stored procedure or user-defined function that takes the table type as a parameter. 

Set up sample type and sproc

```SQL
CREATE TYPE myTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO
CREATE PROCEDURE myProc 
   @p1 myTableType readonly
AS
BEGIN
   SELECT myName from @p1 p
END
```

Calling the sproc: 

```ocaml
type TableValuedSample = 
    SqlCommand<"exec myProc @x", connectionString>
let cmdSp = new TableValuedSample(x = [ 1, Some "monkey"; 2, Some "donkey"])
cmdSp.Execute() |> List.ofSeq

val it : string list = ["monkey"; "donkey"]
```


### Other samples

[WPF Databinding](http://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider/tree/master/DataBinding)

[Web API](http://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider/tree/master/WebApi)

### Library license

The library is available under Apache 2.0. For more information see the [License file] 
(http://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider/blob/master/LICENSE.md
) in the GitHub repository.

