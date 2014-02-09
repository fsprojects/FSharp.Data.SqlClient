(*** hide ***)
#r "../../bin/FSharp.Data.Experimental.SqlCommandProvider.dll"

(**

Features
===============================================

 * Typed access to the result of running a query.
 * Typed access to @parameters needed for running a query
 * Sync/Async execution
 * Results as tuples, records, or DataTable
 * Fields that can be NULL translate to the F# Option type, forcing you to deal with the issue of null values directly.
 * Sql is invalid -> Compiler error!
 * Sql can be inline, or in an external file

Examples 
===============================================
*)

open FSharp.Data.Experimental

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>]
let productsSql = " 
    SELECT TOP (@top) Name AS ProductName, SellStartDate, Size
    FROM Production.Product 
    WHERE SellStartDate > @SellStartDate
"

(**

 * Sync execution
 * Seq of tuples is default result set type

*)

type QueryProductSync = SqlCommand<productsSql, connectionString>

let tuples = QueryProductSync().Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")

for productName, sellStartDate, size in tuples do
    printfn "Product name: %s. Sells start date %A, size: %A" productName sellStartDate size

(**

 * Sequence of custom records as result set
 * ConnectionString can be overridden via constructor

*)

type QueryProductAsRecords = SqlCommand<productsSql, connectionString, ResultType = ResultType.Records>

let recordsCmd = QueryProductAsRecords(connectionString = 
    "Data Source=(local);Initial Catalog=AdventureWorks2012;Integrated Security=True")

recordsCmd.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
|> Async.RunSynchronously 
|> Seq.iter (fun x -> 
    printfn "Product name: %s. Sells start date %A, size: %A" x.ProductName x.SellStartDate x.Size)

(**

 * Typed data table as result set
 * Typed data table can be used to send updates back to database and for data-binding scenarios

*)

type QueryProductDataTable = SqlCommand<productsSql, connectionString, ResultType = ResultType.DataTable>

let dataTableCmd = QueryProductDataTable() 

dataTableCmd.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") 
|> Seq.iter (fun row -> 
    printfn "Product name: %s. Sells start date %O, size: %A" row.ProductName row.SellStartDate row.Size)

(**

 * Single row hint. Must be provided explicitly. Cannot be inferred. 
 * Nullable columns mapped to Option<_> type
 * Calling SQL Table-Valued Function

*)

type QueryPersonInfoSingletoneAsRecords = 
    SqlCommand<
        "SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        ResultType = ResultType.Records, 
        SingleRow = true>

let singletone = new QueryPersonInfoSingletoneAsRecords()

let person = singletone.AsyncExecute(PersonId = 2) |> Async.RunSynchronously 
match person.FirstName, person.LastName with
| Some first, Some last -> printfn "Person id: %i, name: %s %s" person.PersonID first last 
| _ -> printfn "What's your name %i?" person.PersonID

(**

 * Same as previous but using tuples as result type

*)

[<Literal>]
let queryPersonInfoSingletoneQuery = 
    "SELECT PersonID, FirstName, LastName FROM dbo.ufnGetContactInformation(@PersonId)"

type QueryPersonInfoSingletoneTuples = 
    SqlCommand<queryPersonInfoSingletoneQuery, connectionString, SingleRow=true>

QueryPersonInfoSingletoneTuples().Execute(PersonId = 2) 
    |> (function
        | id, Some first, Some last -> printfn "Person id: %i, name: %s %s" person.PersonID first last 
        | id, _, _ -> printfn "What's your name %i?" person.PersonID
    ) 

(**

 * Same as previous but using typed DataTable as result type

*)

type QueryPersonInfoSingletoneDataTable = 
    SqlCommand<
        "SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        ResultType = ResultType.DataTable>

let table = QueryPersonInfoSingletoneDataTable().AsyncExecute(PersonId = 2) |> Async.RunSynchronously 

for row in table do
    printfn "Person info:Id - %i,FirstName - %O,LastName - %O" row.PersonID row.FirstName row.LastName 

(**

 * One column only result set inferred. Combined with SingleRow hint gives single value as result.
 * AsyncExecute/Execute are just regular F# methods. So args can be passed by name or by position.

*)

type QueryPersonInfoSingleValue = 
    SqlCommand<
        "SELECT FirstName + ' '  + LastName FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        SingleRow=true>

let personId = 2
QueryPersonInfoSingleValue().Execute(personId) 
|> Option.iter (fun name -> printf "Person with id %i has name %s" personId name)

(**

 * Single value.
 * Running the same command more than ones with diff params.

*)

type GetServerTime = 
    SqlCommand<
        "IF @IsUtc = CAST(1 AS BIT) SELECT GETUTCDATE() ELSE SELECT GETDATE()", 
        connectionString, 
        SingleRow=true>

let getSrvTime = new GetServerTime()

getSrvTime.AsyncExecute(IsUtc = true) |> Async.RunSynchronously |> printfn "%A"
getSrvTime.Execute(IsUtc = false) |> printfn "%A"

(**

 * Non-query.

*)

[<Literal>]
let invokeSp = "
    EXEC HumanResources.uspUpdateEmployeePersonalInfo 
        @BusinessEntityID, 
        @NationalIDNumber,
        @BirthDate, 
        @MaritalStatus, 
        @Gender
"
type UpdateEmplInfoCommand = SqlCommand<invokeSp, connectionString>
let nonQuery = new UpdateEmplInfoCommand()
let rowsAffected = 
    nonQuery.Execute(
        BusinessEntityID = 2, NationalIDNumber = "245797967", 
        BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F") 

(**

 * Stored procedure by name only.

*)

open System.Data

type UpdateEmplInfoCommandSp = 
    SqlCommand<
        "HumanResources.uspUpdateEmployeePersonalInfo", 
        connectionString, 
        CommandType = CommandType.StoredProcedure >

let sp = new UpdateEmplInfoCommandSp()

sp.AsyncExecute(BusinessEntityID = 2, NationalIDNumber = "245797967", 
    BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F") 
|> Async.RunSynchronously

(**

 * Command from file.
 * Sql files can be edited in Visual Studio or SQL management studio. Both provides IntelliSense with proper setup. 

*)

type CommandFromFile = SqlCommand<"GetDate.sql", connectionString>
let cmd = CommandFromFile()
cmd.Execute() |> ignore

(**

 * Table-valued parameters.

When using TVPs, the Sql command needs to be calling a stored procedure or user-defined function that takes the table type as a parameter. 
Set up sample type and sproc

CREATE TYPE myTableType AS TABLE (myId int not null, myName nvarchar(30) null)

GO

CREATE PROCEDURE myProc 

   @p1 myTableType readonly

AS

BEGIN

   SELECT myName from @p1 p

END


*)

type TableValuedSample = SqlCommand<"exec myProc @x", connectionString>
type TVP = TableValuedSample.MyTableType
let tvpSp = new TableValuedSample()
//nullable columns mapped to optional ctor params
tvpSp.Execute(x = [ TVP(myId = 1, myName = "monkey"); TVP(myId = 2) ]) 

(*

Draft

    * Tuples. Default. Mostly convenient in F# combined with pattern matching
    * Records. .NET-style class with read-only properties. WebAPI/Json.NET/WPF/ASP.NET MVC.
    * DataTable with inferred data rows similar to Records. Update scenarios. WPF data binding.
    * Maps. For rare cases when structure of output cannot be inferred.


*)