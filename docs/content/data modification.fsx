(*** hide ***)
#r @"..\..\src\SqlClient\bin\Debug\FSharp.Data.SqlClient.dll"
#r "System.Transactions"
open FSharp.Data
open System

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

(**

Data modification
===================

Library supports different ways to send data modifications to Sql Server. 


Hand-written DML statements
-------------------------------------

The most obvious approach is to explicitly written DML statements combined with SqlCommandProvider.

*)

type CurrencyCode = 
    SqlEnumProvider<"SELECT Name, CurrencyCode FROM Sales.Currency", connectionString>

do
    use cmd = new SqlCommandProvider<"
        INSERT INTO Sales.CurrencyRate 
        VALUES (@currencyRateDate, @fromCurrencyCode, @toCurrencyCode, 
                @averageRate, @endOfDayRate, DEFAULT) 
    ", connectionString>()

    let recordsInserted = 
        cmd.Execute(
            currencyRateDate = DateTime.Today, 
            fromCurrencyCode = CurrencyCode.``US Dollar``, 
            toCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
            averageRate = 0.63219M, 
            endOfDayRate = 0.63219M) 

    assert (recordsInserted = 1)

(**
This works any kind of data modification statement: _INSERT_, _UPDATE_, _DELETE_, _MERGE_ etc.

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
        ", connectionString, ResultType.Tuples, SingleRow = true>()

    jamesKramerId |> cmd.Execute |> Option.get

assert("Production Technician - WC60" = jobTitle)
    
let newJobTitle = "Uber " + jobTitle

let recordsAffrected = 
    use updatedJobTitle = new AdventureWorks.HumanResources.uspUpdateEmployeeHireInfo()
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
    // Static Create factory method provides better than ctor intellisense.
    // See https://github.com/Microsoft/visualfsharp/issues/449
    use cmd = new AdventureWorks.dbo.ufnGetContactInformation()

    //Use ExecuteSingle if you're sure it return 0 or 1 rows
    let result = cmd.ExecuteSingle(PersonID = jamesKramerId) 
    result.Value.JobTitle.Value

assert(newJobTitle = updatedJobTitle)


(**


Statically typed DataTable
-------------------------------------

Both hand-written T-SQL and stored procedures methods have significant downside - it requires tedious coding. 
It gets worse when different kinds of modification (inserts, updates, deletes, merges) for same entity need to be issued. 
In most cases it forces to have command/stored procedure per modification type. 
`SqlProgrammabilityProvider` offers elegant solution based on ADO.NET [DataTable](https://msdn.microsoft.com/en-us/library/system.data.datatable.aspx) 
class with static types on a top. 
To certain extend this is similar to ancient, almost forgotten [Generating Strongly Typed DataSets](https://msdn.microsoft.com/en-us/library/wha85tzb.aspx) 
technique except that epic F# feature [Type Providers](https://msdn.microsoft.com/en-us/library/hh156509.aspx) 
streamlines the whole development experience. 

Using `Sales.CurrencyRate` table as example let see how's generated table type is different from it's base [DataTable](https://msdn.microsoft.com/en-us/library/system.data.datatable.aspx) type. 

Generated tables type names follow a pattern: _TypeAliasForRoot_._SchemaName_.__Tables__._TableName_
*)

let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
assert (currencyRates.TableName = "[Sales].[CurrencyRate]")

(**
`TableName property` is set to appropriate value. 

Collection of rows is reachable via `Rows` property of type `IList<#DataRow>`.
Familiar list operations: Add, Remove, Insert etc. are available for typed DataTable. 
Typed column accessors are added to existing set of `DataRow` type members. 
An intellisense experience a little clunky but it felt important to keep legacy `DataRow`
type members available for invocation. 

<img src="img/DataRowTypedAccessors.png"/>

*)

let firstRow = currencyRates.Rows.[0]
firstRow.AverageRate

(**
Method `AddRow` adds a new row to a table. 

<img src="img/AddRow.png"/>

- There is 1-1 correspondence between column names/types and the method parameters
- `IDENTITY` column is excluded from parameters list for obvious reasons
- Nullable columns are mappend to parameters of type `option<_>`
- Columns with `DEFAULT` constraint are also represented as parameters of type `option<_>`. 
This is more convenient that specifying DEFAULT as a value in INSERT statement
- Both kinds of parameters - for nullable columns or columns with defaults - can be omitted from invocation
- Minor but nice feature is ability to retrieve `MS_Description` which works only for Sql Server 
  because Sql Azure doesn't support extended properties.  
*)
do
    currencyRates.AddRow(
        CurrencyRateDate = DateTime.Today, 
        FromCurrencyCode = CurrencyCode.``US Dollar``, 
        ToCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
        AverageRate = 0.63219M, 
        EndOfDayRate = 0.63219M)
(** Side-effectful `AddRow` makes easier to add rows in type-safe manner. 
Pair of invocations to `NewRow` and `Rows.Add` can be used as alternative. 
This approach also makes sense if for some reason you need to keep a reference to newly added 
for further manipulations.  
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

With this knowledge in mind example at top the page can be re-written as 
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
refreshed after update from database. This is very cool feature. **It works only for `BatchSize` = 1** which is default. 
Of course it's applicable only to new data rows (ones that cause INSERT statement to be issued). 
Follow [this link](https://msdn.microsoft.com/en-us/library/aadf8fk2.aspx) to find out more about batch updates. 

The snippet below demonstrates update and delete logic. 
Note how combining SqlCommandProvider to load existing data with typed data tables produces simple and safe code. 
*)

do
    use cmd = new SqlCommandProvider<"
        SELECT * 
        FROM Sales.CurrencyRate 
        WHERE FromCurrencyCode = @from
            AND ToCurrencyCode = @to
            AND CurrencyRateDate > @date
        ", connectionString, ResultType.DataReader>()
    //ResultType.DataReader !!!
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    //load data into data table
    cmd.Execute("USD", "GBP", DateTime(2014, 1, 1)) |> currencyRates.Load

    let latestModification =
        //manipulate Rows as any sequence
        currencyRates.Rows
        |> Seq.sortBy (fun x -> x.ModifiedDate)
        |> Seq.last

    latestModification.Delete()
    //or use list operatoin
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

__WARNING__ Unfortunately Update method on typed data table doesn't have asynchronous version. 
That's where command types provided by SqlCommandProvider have distinct advantage.

</p></div>

Bulk Load
-------------------------------------

Another useful scenario for typed data tables is bulk load. 
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

There is one vcase 

Custom update/bulk copy logic
-------------------------------------
Both Update and BulkCopy operations can be configured via parameters they accept (connection, transaction, batchSize etc) 
That said, default update logic provided by typed Data Table can be not sufficient for some advanced scenarios. 
It doesn't mean you need to give up on convenience of static typing. 
Customize update behavior by creating you own instance of [SqlDataAdapter](https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqldataadapter.aspx) 
(or [SqlBulkCopy](https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqlbulkcopy.aspx)) and configuring it to your needs. 

<div class="well well-small" style="margin:0px 70px 0px 20px;">

__TI__ You can find [SqlCommandBuilder](https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqlcommandbuilder.aspx) 
class useful for T-SQL generation.

</p></div>

Pseudo code for custom data adapter:
*)

open System.Data.SqlClient

do
    let currencyRates = new AdventureWorks.Sales.Tables.CurrencyRate()
    //load, update, delete, insert rows 
    // ...
    use adapter = new SqlDataAdapter()
    //configure adapter: setup select command, transaction etc.
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

There is a way to get your hands on typed data table by specifying ResultType.DataTable as output type 
for `SqlCommandProvider` generated command type. 
This approach gives a flexibility at a cost of leaving more room for error. 
To begin with an output projection should be suitable for sending changes back to a database. 
It rules out transformations, extensive joins etc. 
Basically only raw columns for a single table make good candidates for persistable changes. 
Typed data table class you get back by executing command with `ResultType.DataTable` is in large similar to one 
describe above. One noticeable difference is absence of parametrized `AddRow`/`NewRow` method. This is intentional. 
Updating, deleting or merging rows are most likely scenarios where this can be useful. 
For update/delete/merge logic to work properly primary key (or unique index) columns must be included 
into column selection. To insert new records use static data tables types generated by `SqlProgrammbilityProvider`. 
That said it's still possible to add rows with some static typing support. 

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
        ", connectionString, ResultType.DataTable>()
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

