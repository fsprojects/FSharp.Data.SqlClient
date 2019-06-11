(*** hide ***)
#r @"..\..\bin\net40\FSharp.Data.SqlClient.dll"
#r "System.Transactions"
open FSharp.Data
open System

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

(**

Data modification
===================

FSharp.Data.SqlClient supports multiple approaches to send data modifications to Sql Server. 

Hand-written DML statements
-------------------------------------

Write DML statements using `SqlCommandProvider`:

*)

type CurrencyCode = 
    SqlEnumProvider<"SELECT Name, CurrencyCode FROM Sales.Currency", connectionString>

do
    use cmd = new SqlCommandProvider<"
        INSERT INTO Sales.CurrencyRate 
        VALUES (@currencyRateDate, @fromCurrencyCode, @toCurrencyCode, 
                @averageRate, @endOfDayRate, DEFAULT) 
    ", connectionString>(connectionString)

    let recordsInserted = 
        cmd.Execute(
            currencyRateDate = DateTime.Today, 
            fromCurrencyCode = CurrencyCode.``US Dollar``, 
            toCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
            averageRate = 0.63219M, 
            endOfDayRate = 0.63219M) 

    assert (recordsInserted = 1)

(**
This works for any kind of data modification statement: _INSERT_, _UPDATE_, _DELETE_, _MERGE_ etc.

Stored Procedures
-------------------------------------

*)

type AdventureWorks = SqlProgrammabilityProvider<connectionString>

let jamesKramerId = 42

let businessEntityID, jobTitle, hireDate = 
    use cmd = new SqlCommandProvider<"
        SELECT 
	        BusinessEntityID
	        ,JobTitle
	        ,HireDate
        FROM 
            HumanResources.Employee 
        WHERE 
            BusinessEntityID = @id
        ", connectionString, ResultType.Tuples, SingleRow = true>(connectionString)

    jamesKramerId |> cmd.Execute |> Option.get

assert("Production Technician - WC60" = jobTitle)
    
let newJobTitle = "Uber " + jobTitle

let recordsAffrected = 
    use updatedJobTitle = new AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo(connectionString)
    updatedJobTitle.Execute(
        businessEntityID, 
        newJobTitle, 
        hireDate, 
        RateChangeDate = DateTime.Now, 
        Rate = 12M, 
        PayFrequency = 1uy, 
        CurrentFlag = true 
    )

assert(recordsAffrected = 1)

let updatedJobTitle = 
    // Static Create factory method provides better IntelliSense than ctor.
    // See https://github.com/Microsoft/visualfsharp/issues/449
    use cmd = new AdventureWorks.dbo.ufnGetContactInformation(connectionString)

    //Use ExecuteSingle if you're sure it return 0 or 1 rows.
    let result = cmd.ExecuteSingle(PersonID = jamesKramerId) 
    result.Value.JobTitle.Value

assert(newJobTitle = updatedJobTitle)

(**

Statically-typed DataTable
-------------------------------------

Both hand-written T-SQL and stored procedures have a significant downside: it requires tedious coding. 
It gets worse when different kinds of modifications -- inserts, updates, deletes, merges -- need to be issued for the same entity.
In most cases you are forced to have one command/stored procedure per modification type. 
`SqlProgrammabilityProvider` offers an elegant solution based on the ADO.NET [DataTable](https://msdn.microsoft.com/en-us/library/system.data.datatable.aspx) 
class with static types on top. 
To a certain extent, this is similar to the ancient, almost forgotten [Generating Strongly Typed DataSets](https://msdn.microsoft.com/en-us/library/wha85tzb.aspx) 
technique except that the epic F# [Type Providers](https://msdn.microsoft.com/en-us/library/hh156509.aspx) feature
streamlines the whole development experience. 

Using `Sales.CurrencyRate` table as an example, let's see how a generated table type is different from its base [DataTable](https://msdn.microsoft.com/en-us/library/system.data.datatable.aspx) type. 

Generated table type names follow a consistent pattern: _TypeAliasForRoot_._SchemaName_._Tables_._TableName_
*)

let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
assert (currencyRates.TableName = "[Sales].[CurrencyRate]")

(**
The type provider generates an expected value for the `TableName` property. 

The `Rows` property, of type `IList<#DataRow>`, provides access to the rows within the table.
Familiar list operations are available for typed DataTable: Add, Remove, Insert etc.
Typed column accessors are added to the existing set of `DataRow` type members. 
The IntelliSense experience is left a little clunky to retain legacy `DataRow` type members.

<img src="img/DataRowTypedAccessors.png"/>

*)

let firstRow = currencyRates.Rows.[0]
firstRow.AverageRate

(**
It is possible to get a reference to the DataColumn object
*)

let averageRateColumn = currencyRates.Columns.AverageRate


(**
The `AddRow` method adds a new row to a table. 

<img src="img/AddRow.png"/>

- There is 1-1 correspondence between column names/types and the method parameters
- `IDENTITY` column is excluded from parameters list for obvious reasons
- Nullable columns are mappend to parameters of type `option<_>`
- Columns with `DEFAULT` constraint are also represented as parameters of type `option<_>`. 
This is more convenient that specifying DEFAULT as a value in INSERT statement
- Both kinds of parameters -- nullable columns or columns with defaults -- can be omitted from invocation
- Minor but nice feature is the ability to retrieve `MS_Description`, which works only for Sql Server 
  because Sql Azure doesn't support extended properties.  
*)
do
    currencyRates.AddRow(
        CurrencyRateDate = DateTime.Today, 
        FromCurrencyCode = CurrencyCode.``US Dollar``, 
        ToCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
        AverageRate = 0.63219M, 
        EndOfDayRate = 0.63219M)
(** Side-effecting `AddRow` makes it easier to add rows in type-safe manner. 
A pair of invocations to `NewRow` and `Rows.Add` can be used as an alternative. 
This approach also makes sense if for some reason you need to keep a reference to a newly added row for further manipulations.  
*)
do 
    let newRow = 
        currencyRates.NewRow(
            CurrencyRateDate = DateTime.Today, 
            FromCurrencyCode = CurrencyCode.``US Dollar``, 
            ToCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
            AverageRate = 0.63219M, 
            EndOfDayRate = 0.63219M,
            //Column with DEFAULT constraint can be passed in explicitly
            ModifiedDate = Some DateTime.Today
        )
    currencyRates.Rows.Add newRow

(**

With this knowledge in mind, the example at top the page can be re-written as follows:
*)

do
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    let newRow = 
        currencyRates.NewRow(
            CurrencyRateDate = DateTime.Today, 
            FromCurrencyCode = "USD", 
            ToCurrencyCode = "GBP", 
            AverageRate = 0.63219M, 
            EndOfDayRate = 0.63219M
        )
    currencyRates.Rows.Add newRow
    //Call Update to push changes to a database
    let recordsAffected = currencyRates.Update()
    assert(recordsAffected = 1)
    printfn "ID: %i, ModifiedDate: %O" newRow.CurrencyRateID newRow.ModifiedDate

(**
- Call to `Update` is required to push changes into a database 
- `CurrencyRateID` IDENTITY column and all fields with DEFAULT constraints that didn't have value specified are 
refreshed after an update from the database. This is a very cool feature. **It works only for `BatchSize` = 1**, which is the default. 
Of course it's applicable only to new data rows (that issue an INSERT statement). 
Follow [this link](https://msdn.microsoft.com/en-us/library/aadf8fk2.aspx) to find out more about batch updates. 

The snippet below demonstrates update and delete logic. 
Note how combining `SqlCommandProvider` to load existing data with typed data tables produces simple and safe code. 
*)

do
    use cmd = new SqlCommandProvider<"
        SELECT * 
        FROM Sales.CurrencyRate 
        WHERE FromCurrencyCode = @from
            AND ToCurrencyCode = @to
            AND CurrencyRateDate > @date
        ", connectionString, ResultType.DataReader>(connectionString)
    //ResultType.DataReader !!!
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    //load data into data table
    cmd.Execute("USD", "GBP", DateTime(2014, 1, 1)) |> currencyRates.Load

    let latestModification =
        //manipulate Rows as a sequence
        currencyRates.Rows
        |> Seq.sortBy (fun x -> x.ModifiedDate)
        |> Seq.last

    latestModification.Delete()
    //or use list operation
    //currencyRates.Rows.Remove latestModification

    //adjust rates slightly
    for row in currencyRates.Rows do
        if row.RowState <> System.Data.DataRowState.Deleted
        then 
            row.EndOfDayRate <- row.EndOfDayRate + 0.01M
            row.ModifiedDate <- DateTime.Today

    let totalRecords = currencyRates.Rows.Count
    // custom batch size - send them all at once
    let recordsAffected = currencyRates.Update(batchSize = totalRecords) 
    
    assert (recordsAffected = totalRecords)

(**

<div class="well well-small" style="margin:0px 70px 0px 20px;">

__WARNING__ Unfortunately, the `Update` method on the typed data table doesn't have an asynchronous version. 
Command types provided by SqlCommandProvider have distinct advantage when you need asynchronous invocation.

</p></div>

Bulk Load
-------------------------------------

Bulk loading is another useful scenario for typed data tables. 
It looks exactly like adding new rows except at the end you make a call to `BulkCopy` instead of `Update`.
*)

do
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    let newRow = 
        currencyRates.NewRow(
            CurrencyRateDate = DateTime.Today, 
            FromCurrencyCode = "USD", 
            ToCurrencyCode = "GBP", 
            AverageRate = 0.63219M, 
            EndOfDayRate = 0.63219M,
            ModifiedDate = DateTime.Today
        )

    currencyRates.Rows.Add newRow
    //Insert many more rows here
    currencyRates.BulkCopy(copyOptions = System.Data.SqlClient.SqlBulkCopyOptions.TableLock)

(**

Custom update/bulk copy logic
-------------------------------------
Both `Update` and `BulkCopy` operations can be configured via parameters, i.e. connection, transaction, batchSize, etc.
That said, default update logic provided by typed DataTable can be insufficient for some advanced scenarios. 
You don't need to give up on convenience of static typing, however. You can also 
customize update behavior by creating your own instance of [SqlDataAdapter](https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqldataadapter.aspx) 
(or [SqlBulkCopy](https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqlbulkcopy.aspx)) and configuring it to your needs. 

Pseudocode for custom data adapter:
*)

open System.Data.SqlClient

do
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    //load, update, delete, insert rows 
    // ...
    use adapter = new SqlDataAdapter()
    //configure adapter: setup select, insert, update, delete commands, transaction etc.
    // ...
    adapter.Update( currencyRates) |> ignore

//Similarly for custom bulk copy:
    
do
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    //load, update, delete, insert rows 
    // ...
    //configure bulkCopy: copyOptions, connectoin, transaction, timeout, batch size etc.
    use bulkCopy = new SqlBulkCopy(AdventureWorks.Sales.Tables.CurrencyRate.ConnectionStringOrName)
    // ...
    bulkCopy.WriteToServer( currencyRates) |> ignore



(**
Transaction and connection management
-------------------------------------
Please read [Transactions](transactions.html) chapter of the documentation.
Pay particular attention to *DataTable Updates/Bulk Load* section.

Query-derived tables
-------------------------------------

You can get your hands on a typed data table by specifying ResultType.DataTable as the output type 
for `SqlCommandProvider` generated command types. 
This approach gives flexibility at a cost of leaving more room for error. 
An output projection should be suitable for sending changes back to a database. 
It rules out transformations, extensive joins etc. 
Only raw columns for a single table make good candidates for persistable changes. 
The typed `DataTable` class you get back by executing a command with `ResultType.DataTable` is largely similar to the one 
describe above. One noticeable difference is the absence of the parametrized `AddRow`/`NewRow` method. This is intentional. 
Updating, deleting or merging rows are the most likely scenarios where this can be useful. 
For update/delete/merge logic to work properly, primary key (or unique index) columns must be included 
in column selection. To insert new records, use static data table types generated by `SqlProgrammbilityProvider`. 
That said, it's still possible to add rows with some static typing support. 

One of the examples above can be re-written as 
*)

do
    //CurrencyRateID is included
    use cmd = new SqlCommandProvider<"
        SELECT 
            CurrencyRateID,
            CurrencyRateDate,
            FromCurrencyCode,
            ToCurrencyCode,
            AverageRate,
            EndOfDayRate
        FROM Sales.CurrencyRate 
        WHERE FromCurrencyCode = @from
            AND ToCurrencyCode = @to
            AND CurrencyRateDate > @date
        ", connectionString, ResultType.DataTable>(connectionString)
    //ResultType.DataTable !!!
    let currencyRates = cmd.Execute("USD", "GBP", DateTime(2014, 1, 1)) 

    let latestModification =
        currencyRates.Rows
        |> Seq.sortBy (fun x -> x.CurrencyRateDate)
        |> Seq.last
    
    //Delete
    latestModification.Delete()

    //Update
    for row in currencyRates.Rows do
        if row.RowState <> System.Data.DataRowState.Deleted
        then 
            row.EndOfDayRate <- row.EndOfDayRate + 0.01M

    //Insert
    let newRecord = currencyRates.NewRow()
    newRecord.CurrencyRateDate <- DateTime.Today
    newRecord.FromCurrencyCode <- "USD"
    newRecord.ToCurrencyCode <- "GBP"
    newRecord.AverageRate <- 0.63219M
    newRecord.EndOfDayRate <- 0.63219M

    currencyRates.Rows.Add newRecord

    let totalRecords = currencyRates.Rows.Count

    let recordsAffected = currencyRates.Update(batchSize = totalRecords) 
    assert (recordsAffected = totalRecords)

