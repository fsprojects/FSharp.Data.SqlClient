(*** hide ***)
#r @"..\..\bin\net40\FSharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types.dll"
open FSharp.Data
[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

(**

Controlling output
===============================================
*)


//Connection and query definition are shared for most of the examples below

[<Literal>]
let productsSql = " 
    SELECT TOP (@top) Name AS ProductName, SellStartDate, Size
    FROM Production.Product 
    WHERE SellStartDate > @SellStartDate
"

(**
 * Sequence of custom records is default result set type. 
*)

type QueryProductAsRecords = SqlCommandProvider<productsSql, connectionString>
let queryProductAsRecords = new QueryProductAsRecords(connectionString)

let records = 
    queryProductAsRecords.AsyncExecute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01")
    |> Async.RunSynchronously 
    |> List.ofSeq

records |> Seq.iter (printfn "%A")

(**
 These records implement `DynamicObject` for easy binding and JSON.NET serialization and `Equals` for structural equality.

 * Sync execution
 * Seq of tuples as result set type
 * Consider ResultType.Tuples to work around unique column name limitation for ResultType.Records.  
*)

type QueryProductSync = SqlCommandProvider<productsSql, connectionString, ResultType = ResultType.Tuples>

do
    use cmd = new QueryProductSync(connectionString)
    let tuples = cmd.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") 

    for productName, sellStartDate, size in tuples do
        printfn "Product name: %s. Sells start date %A, size: %A" productName sellStartDate size

(**
 * Typed data table as result set
 * DataTable result type is an enabler for data binding and update scenarios. Look at [data modification](data modification.html) for details.
*)

do 
    use cmd = new SqlCommandProvider<productsSql, connectionString, ResultType.DataTable>(connectionString)
    let table = cmd.Execute(top = 7L, SellStartDate = System.DateTime.Parse "2002-06-01") 
    for row in table.Rows do
        printfn "Product name: %s. Sells start date %O, size: %A" row.ProductName row.SellStartDate row.Size

(**
 * Single row hint. Must be provided explicitly. Cannot be inferred
 * Nullable columns mapped to `Option<_>` type
 * Calling SQL Table-Valued Function
*)

type QueryPersonInfoSingletoneAsRecords = 
    SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)"
                        , connectionString
                        , SingleRow = true>

let singleton = new QueryPersonInfoSingletoneAsRecords(connectionString)

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
    SqlCommandProvider<queryPersonInfoSingletoneQuery, connectionString, ResultType.Tuples, SingleRow=true>

QueryPersonInfoSingletoneTuples
    .Create(connectionString)
    .Execute(PersonId = 2).Value
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

do 
    use cmd = new QueryPersonInfoSingletoneDataTable(connectionString)
    let table = cmd .AsyncExecute(PersonId = 2) |> Async.RunSynchronously 
    for row in table.Rows do
        printfn "Person info:Id - %i,FirstName - %O,LastName - %O" row.PersonID row.FirstName row.LastName 

// you can refer to the table type
let table2 : QueryPersonInfoSingletoneDataTable.Table = 
    let cmd = new QueryPersonInfoSingletoneDataTable(connectionString)
    cmd.Execute(PersonId = 2)

// you can refer to the row type
for row : QueryPersonInfoSingletoneDataTable.Table.Row in table2.Rows do
    printfn "Person info:Id - %i,FirstName - %O,LastName - %O" row.PersonID row.FirstName row.LastName 


(**

 * Same as previous but using `SqlProgrammabilityProvider<...>`
 * Worth noting that Stored Procedure/Function generated command instances have explicit ExecuteSingle/ AsyncExecuteSingle methods because there is no single place to specify SingleRow=true as for SqlCommandProvider. 
*)

type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>
do
    use cmd = new AdventureWorks2012.dbo.ufnGetContactInformation(connectionString)
    cmd.ExecuteSingle(1) //opt-in for explicit call to 
    |> Option.iter(fun x ->  
        printfn "Person info:Id - %i,FirstName - %O,LastName - %O" x.PersonID x.FirstName x.LastName 
    )

(**

 * One column only result set is inferred. Combined with `SingleRow` hint it gives single value as result
 * `AsyncExecute/Execute` are just regular F# methods, so args can be passed by name or by position

*)

type QueryPersonInfoSingleValue = 
    SqlCommandProvider<
        "SELECT FirstName + ' '  + LastName FROM dbo.ufnGetContactInformation(@PersonId)", 
        connectionString, 
        SingleRow=true>

do 
    let personId = 2
    use cmd = new QueryPersonInfoSingleValue(connectionString)
    cmd.Execute( personId)
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

let getSrvTime = new GetServerTime(connectionString)

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
let nonQuery = new UpdateEmplInfoCommand(connectionString)
let rowsAffected = 
    nonQuery.Execute(
        BusinessEntityID = 2, NationalIDNumber = "245797967", 
        BirthDate = System.DateTime(1965, 09, 01), MaritalStatus = "S", Gender = "F") 

(**

 * Non-query with MS SQL HierarchyId using `SqlProgrammabilityProvider<...>`

*)
open System
open System.Data
open Microsoft.SqlServer.Types

do 
    use cmd = new AdventureWorks2012.HumanResources.uspUpdateEmployeeLogin(connectionString)
    let hierarchyId = SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/"))
    cmd.Execute(
        BusinessEntityID = 291, 
        CurrentFlag = true, 
        HireDate = DateTime(2013,1,1), 
        JobTitle = "gatekeeper", 
        LoginID = "adventure-works\gat0", 
        OrganizationNode = hierarchyId
    )
    |> printfn "Records afftected: %i"

(**
### Result sequence is un-buffered by default 

Although it implements standard `seq<_>` (`IEnumerable<_>`) interface it can be evaluated only once. 
It is done mostly for memory efficiency. It behaves as forward-only cursor similar to underlying SqlDataReader. 
If multiple passes over the sequence required use standard `Seq.cache` combinator. 
*)

type Get42 = SqlCommandProvider<"SELECT * FROM (VALUES (42), (43)) AS T(N)", connectionString>
let xs = (new Get42(connectionString)).Execute() |> Seq.cache 
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
    open Microsoft.Data.SqlClient
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
