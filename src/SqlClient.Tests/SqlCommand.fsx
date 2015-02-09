(**
    Use cases
*)
#r "../../bin/FSharp.Data.SqlClient.dll"
#load "ConnectionStrings.fs"
open System
open FSharp.Data

[<Literal>] 
let connectionString = ConnectionStrings.AdventureWorksLiteralMultipleActiveResults

[<Literal>]
let queryProductsSql = "
SELECT TOP (@top) Name AS ProductName, SellStartDate, Size
FROM Production.Product 
WHERE SellStartDate > @SellStartDate
"

//Custom record types and connection string override
type QueryProducts = SqlCommandProvider<queryProductsSql, connectionString>
let cmd1 = new QueryProducts(connectionString = ConnectionStrings.AdventureWorksLiteral)
let result1 : Async<QueryProducts.Record seq> = cmd1.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
result1 |> Async.RunSynchronously |> Seq.iter (fun x -> printfn "Product name: %s. Sells start date %A, size: %A" x.ProductName x.SellStartDate x.Size)
let records = cmd1.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") |> List.ofSeq
records |> Seq.iter (printfn "%A")
let record = records.Head

//Record constructor
let newrecord = QueryProducts.Record("foo", System.DateTime(2000,1,1), Some "bar")
record <> newrecord

//Two parallel executions
type cmdType = SqlCommandProvider<queryProductsSql, connectionString>
let par = new cmdType()
let reader1 = par.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let reader2 = par.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
reader1 |> Async.RunSynchronously |> Seq.head |> printfn "%A"
reader2 |> Async.RunSynchronously |> Seq.head |> printfn "%A"

//Tuples
type QueryProductsAsTuples = SqlCommandProvider<queryProductsSql, connectionString, ResultType = ResultType.Tuples>
let cmd = new QueryProductsAsTuples()
let result = cmd.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
result |> Async.RunSynchronously |> Seq.iter (fun(productName, sellStartDate, size) -> printfn "Product name: %s. Sells start date %A, size: %A" productName sellStartDate size)
cmd.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") |> Seq.iter (fun(productName, sellStartDate, size) -> printfn "Product name: %s. Sells start date %A, size: %A" productName sellStartDate size)

//DataTable for data binding scenarios and update
type QueryProductDataTable = SqlCommandProvider<queryProductsSql, connectionString, ResultType = ResultType.DataTable>
let cmd2 = new QueryProductDataTable() //top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
let result2  = cmd2.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") |> Async.RunSynchronously
result2.Rows |> Seq.iter (fun row -> printfn "Product name: %s. Sells start date %O, size: %A" row.ProductName row.SellStartDate row.Size)
cmd2.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01").Rows |> Seq.iter (fun row -> printfn "Product name: %s. Sells start date %O, size: %A" row.ProductName row.SellStartDate row.Size)

//Single row hint and optional output columns. Records result type.
type QueryPersonInfoSingletone = SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, ResultType = ResultType.Records, SingleRow=true>
let cmd3 = new QueryPersonInfoSingletone()
let result3 = cmd3.AsyncExecute(PersonId = 2) 
result3 |> Async.RunSynchronously |> Option.get |> fun x -> printfn "Person info: Id - %i, FirstName - %O, LastName - %O, JobTitle - %O, BusinessEntityType - %O" x.PersonID x.FirstName x.LastName x.JobTitle x.BusinessEntityType
cmd3.Execute(PersonId = 2).Value |> fun x -> printfn "Person info: Id - %i, FirstName - %O, LastName - %O, JobTitle - %O, BusinessEntityType - %O" x.PersonID x.FirstName x.LastName x.JobTitle x.BusinessEntityType

//Single row hint and optional output columns. Tuple result type.
type QueryPersonInfoSingletoneTuples = SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, SingleRow=true, ResultType = ResultType.Tuples>
let cmd35 = new QueryPersonInfoSingletoneTuples()
let result35 : Async<_> = cmd35.AsyncExecute(PersonId = 2) 
result35 |> Async.RunSynchronously |> Option.get |> fun(personId, firstName, lastName, jobTitle, businessEntityType) -> printfn "Person info: Id - %i, FirstName - %O, LastName - %O, JobTitle - %O, BusinessEntityType - %O" personId firstName lastName jobTitle businessEntityType
cmd35.Execute(PersonId = 2).Value |> fun(personId, firstName, lastName, jobTitle, businessEntityType) -> printfn "Person info: Id - %i, FirstName - %O, LastName - %O, JobTitle - %O, BusinessEntityType - %O" personId firstName lastName jobTitle businessEntityType

//Single row hint and optional output columns. Single value.
type QueryPersonInfoSingleValue = SqlCommandProvider<"SELECT FirstName FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, SingleRow=true>
let cmd36 = new QueryPersonInfoSingleValue()
let result36 : Async<_> = cmd36.AsyncExecute(PersonId = 2) 
result36 |> Async.RunSynchronously |> (function | Some(Some firstName) -> printfn "FirstName - %s" firstName | _ -> printfn "Nothing to print" )
cmd36.Execute(PersonId = 2) |> (function | Some(Some firstName) -> printfn "FirstName - %s" firstName | _ -> printfn "Nothing to print" )

//Single row hint and optional output columns. Data table result type.
type QueryPersonInfoSingletoneDataTable = SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString, ResultType = ResultType.DataTable>
let cmd37 = new QueryPersonInfoSingletoneDataTable()
let result37 = cmd37.AsyncExecute(PersonId = 2) |> Async.RunSynchronously 
let printPersonInfo(x : QueryPersonInfoSingletoneDataTable.Row) = printfn "Person info: Id - %i, FirstName - %O, LastName - %O, JobTitle - %O, BusinessEntityType - %O" x.PersonID x.FirstName x.LastName x.JobTitle x.BusinessEntityType
result37.Rows |> Seq.iter printPersonInfo
result37.Rows.[0].FirstName <- result37.Rows.[0].FirstName |> Option.map (fun x -> x + "1")
result37.Rows |> Seq.iter printPersonInfo
result37.Rows.[0].FirstName <- None
result37.Rows |> Seq.iter printPersonInfo
cmd37.Execute(PersonId = 2).Rows |> Seq.iter printPersonInfo

//Single value
type GetServerTime = SqlCommandProvider<"IF @IsUtc = CAST(1 AS BIT) SELECT GETUTCDATE() ELSE SELECT GETDATE()", connectionString, SingleRow=true>
let getSrvTime = new GetServerTime()
let result5 = getSrvTime.AsyncExecute(IsUtc = true) 
result5 |> Async.RunSynchronously |> printfn "%A"
getSrvTime.Execute(IsUtc = false) |> printfn "%A"

//Non-query
type UpdateEmplInfoCommand = SqlCommandProvider<"EXEC HumanResources.uspUpdateEmployeePersonalInfo @BusinessEntityID, @NationalIDNumber,@BirthDate, @MaritalStatus, @Gender ", connectionString>
let cmd4 = new UpdateEmplInfoCommand()
let result4 : Async<int> = cmd4.AsyncExecute(BusinessEntityID = 2, NationalIDNumber = "245797967", BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F") 
let rowsAffected = result4 |> Async.RunSynchronously 
let cmd45 = new UpdateEmplInfoCommand()
cmd45.Execute(BusinessEntityID = 2, NationalIDNumber = "245797967", BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "M")

//Command from file
type q = SqlCommandProvider<"sampleCommand.sql", connectionString>
let cmdFromFile = new q()
cmdFromFile.Execute() |> Seq.toArray

//Fallback to metadata retrieval through FMTONLY
type UseFMTONLY = SqlCommandProvider<"exec dbo.[Init]", connectionString>
let useFMTONLY = new UseFMTONLY()
useFMTONLY.Execute()

//Runtime column names
type UseGet = SqlCommandProvider<"exec dbo.[Get]", connectionString, ResultType = ResultType.DataReader >
let useGet = new UseGet()
useGet.Execute().NextResult() = false

//Insert command
type InsertCommand = 
    SqlCommandProvider<"INSERT INTO dbo.ErrorLog
                VALUES (GETDATE(), @UserName, @ErrorNumber, @ErrorSeverity, @ErrorState, @ErrorProcedure, @ErrorLine, @ErrorMessage)", connectionString, SingleRow = true>

open System.Security.Principal

let cmdInsert = new InsertCommand()
let user = WindowsIdentity.GetCurrent().Name
cmdInsert.Execute(user, 121, 16, 3, "insert test", int __LINE__, "failed insert")

#r "Microsoft.SqlServer.Types"

type GetEmployeeByLevel = SqlCommandProvider<"SELECT * FROM HumanResources.Employee WHERE OrganizationLevel = @OrganizationLevel", connectionString>
let getEmployeeByLevel = new GetEmployeeByLevel()
getEmployeeByLevel.Execute(2s)


type MyCommand1 = SqlCommandProvider<"SELECT GETDATE() AS Now, GETUTCDATE() AS UtcNow",  ConnectionStrings.LocalDbDefault>
type MyRecord1 = MyCommand1.Record
let r1 = MyCommand1.Record(DateTime.Now, DateTime.UtcNow)

type MyCommand2 = SqlCommandProvider<"SELECT GETDATE() AS Now, GETUTCDATE() AS UtcNow",  ConnectionStrings.LocalDbDefault>
let r2 = MyCommand2.Record(DateTime.Now, DateTime.UtcNow)

type MyRecord = { Now: DateTime; UtcNow: DateTime }

let inline toMyRecord (x: 'Recrod) = 
    {
        Now = (^Record : (member get_Now : unit -> DateTime) x)
        UtcNow = (^Record : (member get_UtcNow : unit -> DateTime) x)
    }
