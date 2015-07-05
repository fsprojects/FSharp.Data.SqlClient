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
    // Static Create factory method because it provides better intellisense.
    // See https://github.com/Microsoft/visualfsharp/issues/449
    use cmd = AdventureWorks.dbo.ufnGetContactInformation.Create()

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
This makes available for typed DataTable familiar list operations: Add, Remove, Insert etc. 
Typed column accessors are added to existing set of `DataRow` type members. 
It make the intellisense experience a little clunky but it felt important to keep legacy `DataRow`
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
- Columns with `DEFAULT` constraint or accepting `NULL` as a value is type of `option<_>`
- Extra icing is ability to retrieve `MS_Description` which works only for Sql Server 
  because Sql Azure doesn't support extended properties.  
*)
do
    currencyRates.AddRow(
        CurrencyRateDate = DateTime.Today, 
        FromCurrencyCode = CurrencyCode.``US Dollar``, 
        ToCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
        AverageRate = 0.63219M, 
        EndOfDayRate = 0.63219M)
(** Although side-effectful `AddRow` makes easier to add rows in type-safe manner. 
Pair of invocations to NewRow and Rows.Add can be used as alternative. 
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
            FromCurrencyCode = CurrencyCode.``US Dollar``, 
            ToCurrencyCode = CurrencyCode.``United Kingdom Pound``, 
            AverageRate = 0.63219M, 
            EndOfDayRate = 0.63219M
        )
    currencyRates.Rows.Add newRow
    let recordsAffected = currencyRates.Update()
    assert(recordsAffected = 1)
    printfn "ID: %i, ModifiedDate: %O" newRow.CurrencyRateID newRow.ModifiedDate

(**
    
*)