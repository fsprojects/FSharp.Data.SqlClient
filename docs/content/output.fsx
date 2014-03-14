(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"

(**

Controlling output
===============================================
*)

open FSharp.Data

//Connection and query definition are shared for most of the examples below

[<Literal>]
let connectionString = 
    @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>]
let productsSql = " 
    SELECT TOP (@top) Name AS ProductName, SellStartDate, Size
    FROM Production.Product 
    WHERE SellStartDate > @SellStartDate
"

(**
 * Sequence of custom records is default result set type
*)

type QueryProductAsRecords = SqlCommandProvider<productsSql, connectionString>
let queryProductAsRecords = QueryProductAsRecords()

queryProductAsRecords.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
|> Async.RunSynchronously 
|> Seq.iter (fun x -> 
    printfn "Product name: %s. Sells start date %A, size: %A" x.ProductName x.SellStartDate x.Size)

(**
 * Sync execution
 * Seq of tuples as result set type
*)

type QueryProductSync = SqlCommandProvider<productsSql, connectionString, ResultType = ResultType.Tuples>

let tuples = QueryProductSync().Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")

for productName, sellStartDate, size in tuples do
    printfn "Product name: %s. Sells start date %A, size: %A" productName sellStartDate size

(**
 * Typed data table as result set
*)

type QueryProductDataTable = 
    SqlCommandProvider<productsSql, connectionString, ResultType = ResultType.DataTable>

QueryProductDataTable().Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") 
|> Seq.iter (fun row -> 
    printfn "Product name: %s. Sells start date %O, size: %A" row.ProductName row.SellStartDate row.Size)

(**
 * Single row hint. Must be provided explicitly. Cannot be inferred
 * Nullable columns mapped to `Option<_>` type
 * Calling SQL Table-Valued Function
*)

type QueryPersonInfoSingletoneAsRecords = 
    SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)"
                        , connectionString
                        , SingleRow = true>

let singleton = new QueryPersonInfoSingletoneAsRecords()

let person = singleton.AsyncExecute(PersonId = 2) |> Async.RunSynchronously |> Option.get
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
    SqlCommandProvider<queryPersonInfoSingletoneQuery, connectionString, SingleRow=true, ResultType = ResultType.Tuples>

QueryPersonInfoSingletoneTuples().Execute(PersonId = 2).Value
    |> (function
        | id, Some first, Some last -> printfn "Person id: %i, name: %s %s" person.PersonID first last 
        | id, _, _ -> printfn "What's your name %i?" person.PersonID
    ) 

(**

 * Same as previous but using typed DataTable as result type

*)

type QueryPersonInfoSingletoneDataTable = 
    SqlCommandProvider<
        "SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        ResultType = ResultType.DataTable>

let table = QueryPersonInfoSingletoneDataTable().AsyncExecute(PersonId = 2) |> Async.RunSynchronously 

for row in table do
    printfn "Person info:Id - %i,FirstName - %O,LastName - %O" row.PersonID row.FirstName row.LastName 

(**

 * Same as previous but using `SqlProgrammabilityProvider<...>`

*)

type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>

let db = AdventureWorks2012()

let f = db.Functions.``dbo.ufnGetContactInformation``.AsyncExecute(1) 
        |> Async.RunSynchronously 
        |> Seq.exactlyOne
printfn "Person info:Id - %i,FirstName - %O,LastName - %O" f.PersonID f.FirstName f.LastName 

(**

 * One column only result set is inferred. Combined with `SingleRow` hint it gives single value as result
 * `AsyncExecute/Execute` are just regular F# methods, so args can be passed by name or by position

*)

type QueryPersonInfoSingleValue = 
    SqlCommandProvider<
        "SELECT FirstName + ' '  + LastName FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        SingleRow=true>

let personId = 2
QueryPersonInfoSingleValue().Execute(personId)
|> Option.iter (fun name -> printf "Person with id %i has name %s" personId name.Value)

(**

 * Single value
 * Running the same command more than once with diff params

*)

type GetServerTime = 
    SqlCommandProvider<
        "IF @IsUtc = CAST(1 AS BIT) SELECT GETUTCDATE() ELSE SELECT GETDATE()", 
        connectionString, 
        SingleRow=true>

let getSrvTime = new GetServerTime()

getSrvTime.AsyncExecute(IsUtc = true) |> Async.RunSynchronously |> printfn "%A"
getSrvTime.Execute(IsUtc = false) |> printfn "%A"

(**

 * Non-query

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
type UpdateEmplInfoCommand = SqlCommandProvider<invokeSp, connectionString>
let nonQuery = new UpdateEmplInfoCommand()
let rowsAffected = 
    nonQuery.Execute(
        BusinessEntityID = 2, NationalIDNumber = "245797967", 
        BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F") 

(**

 * Non-query with MS SQL HierarchyId using `SqlProgrammabilityProvider<...>`

*)
#r "../../bin/Microsoft.SqlServer.Types.dll"

open System
open System.Data
open Microsoft.SqlServer.Types

let hierarchyId = SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/"))
let res = db.``Stored Procedures``.``HumanResources.uspUpdateEmployeeLogin``
            .AsyncExecute(291, true, DateTime(2013,1,1), "gatekeeper", "adventure-works\gat0", hierarchyId)
            |> Async.RunSynchronously 
res.ReturnValue

(**
### Result sequence is un-buffered by default 

Although it implements standard `seq<_>` (`IEnumerable<_>`) interface it can be evaluated only once. 
It is done mostly for memory efficiency. It behaves as forward-only cursor similar to underlying SqlDataReader. 
If multiple passes over the sequence required use standard `Seq.cache` combinator. 
*)

type Get42 = SqlCommandProvider<"SELECT * FROM (VALUES (42), (43)) AS T(N)", connectionString>
let xs = Get42().Execute() |> Seq.cache 
printfn "#1: %i " <| Seq.nth 0 xs 
printfn "#2: %i " <| Seq.nth 1 xs //see it fails here if result is not piped into Seq.cache 

(**
### Output result types summary:
    
* Records (default) .NET-style class with read-only properties. WebAPI/ASP.NET MVC/Json.NET/WPF, Data Binding
* Tuples - convenient option for F# combined with pattern matching
* DataTable with inferred data rows similar to Records. Update scenarios. WPF data binding
* DataReader - for rare cases when structure of output cannot be inferred

In later case, resulting `SqlDataReader` can be wrapped into something like that:
*)

module SqlDataReader =  
    open System.Data.SqlClient
    let toMaps (reader: SqlDataReader) = 
        seq {
            use __ = reader
            while reader.Read() do
                yield [
                    for i = 0 to reader.FieldCount - 1 do
                        if not( reader.IsDBNull(i)) 
                        then yield reader.GetName(i), reader.GetValue(i)
                ] |> Map.ofList 
        }

(**
Note that combined with `|> Map.tryFind(key)` this approach can be used to achieve `Option` semantics 
for each row, in other words, such function will return `None` for `NULL` values. Keep in mind though that
the incorrect column name will also return `None`.
The same approach can be used to produce `ExpandoObject` for dynamic scenarios.
*)
