(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"

(**
Not your grandfather's ORM
===================

The library is a home for three type providers:

- __SqlCommandProvider__ - type-safe access to full set of T-SQL language
- __SqlProgrammabilityProvider__ - quick access to Sql Server functions, stored procedures and tables in idiomatic F# way
- __SqlEnumProvider__ - generates enumeration types based on static lookup data from any ADO.NET complaint source

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The FSharp.Data.SqlClient library can be <a href="http://www.nuget.org/packages/FSharp.Data.SqlClient">installed from NuGet</a>:
      <pre>PM> Install-Package FSharp.Data.SqlClient</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

SqlCommandProvider
-------------------------------------

All examples based on [AdventureWorks](http://msftdbprodsamples.codeplex.com/) sample database. 
*)

open FSharp.Data

[<Literal>]
let connectionString = 
    @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

(**

The query below retrieves top 3 sales representatives from North American region who have sales YTD of more than one million. 

*)

do
    use cmd = new SqlCommandProvider<"
        SELECT TOP(@topN) FirstName, LastName, SalesYTD 
        FROM Sales.vSalesPerson
        WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
        ORDER BY SalesYTD
        " , connectionString>()

    cmd.Execute(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) |> printfn "%A"

//output
//seq
//    [("Pamela", "Ansman-Wolfe", 1352577.1325M);
//     ("David", "Campbell", 1573012.9383M);
//     ("Tete", "Mensa-Annan", 1576562.1966M)]

(**

SqlProgrammabilityProvider
-------------------------------------
*)

type AdventureWorks = SqlProgrammabilityProvider<connectionString>
do
    use cmd = new AdventureWorks.dbo.uspGetWhereUsedProductID()
    for x in cmd.Execute( StartProductID = 1, CheckDate = System.DateTime(2013,1,1)) do
        //check for nulls
        match x.ProductAssemblyID, x.StandardCost, x.TotalQuantity with 
        | Some prodAsmId, Some cost, Some qty -> 
            printfn "ProductAssemblyID: %i, StandardCost: %M, TotalQuantity: %M" prodAsmId cost qty
        | _ -> ()

//output
//ProductAssemblyID: 749, StandardCost: 2171.2942, TotalQuantity: 1.00
//ProductAssemblyID: 750, StandardCost: 2171.2942, TotalQuantity: 1.00
//ProductAssemblyID: 751, StandardCost: 2171.2942, TotalQuantity: 1.00
//...

(**

SqlEnumProvider
-------------------------------------
Let's say we need to retrieve number of orders shipped by a certain shipping method since specific date.
*)

//by convention: first column is Name, second is Value
type ShipMethod = SqlEnumProvider<"
    SELECT Name, ShipMethodID FROM Purchasing.ShipMethod ORDER BY ShipMethodID", connectionString>

//Combine with SqlCommandProvider
do 
    use cmd = new SqlCommandProvider<"
        SELECT COUNT(*) 
        FROM Purchasing.PurchaseOrderHeader 
        WHERE ShipDate > @shippedLaterThan AND ShipMethodID = @shipMethodId
    ", connectionString, SingleRow = true>() 
    //overnight orders shipped since Jan 1, 2008 
    cmd.Execute( System.DateTime( 2008, 1, 1), ShipMethod.``OVERNIGHT J-FAST``) |> printfn "%A"
    //output
    //Some (Some 1085)

(**

System requirements
-------------------------------------

 * .NET 4.0 and higher
 * _SqlCommandProvider and SqlProgrammabilityProvider only_ SQL Server 2012 and up or SQL Azure Database at compile-time 
 * SqlEnumProvider works with any ADO.NET complain data-source

SqlCommandProvider and SqlProgrammabilityProvider features at glance
-------------------------------------

* Static type with 2 methods per `SqlCommandProvider<...>` declaration:
    * `AsyncExecute` - for scalability scenarios 
    * `Execute` - convenience when needed
* Configuration
    * Command text (sql script) can be either literal or path to *.sql file
    * Connection string is either literal or name from config file (app.config is default for config file)
    * Connection string can be overridden at run-time via constructor optional parameter
    * Constructor optionally accepts `SqlTransaction` and uses associated connection to execute command
    * "ResolutionFolder" parameter - a folder to be used to resolve relative file paths at compile time. Applied to command text *.sql files only.
* Input:
    * Unbound sql variables/input parameters mapped to mandatory typed arguments for `AsyncExecute/Execute`
    * Set `AllParametersOptional` to true to make all parameters optional (nullable) (`SqlCommandProvider<...>` only)
    * Stored Procedures and User-Defined Functions can be easily discovered with `SqlProgrammabilityProvider<...>`
    * `SqlProgrammabilityProvider<...>` infers default values for input parameters
* Output:
    * Inferred static type for output. Configurable choice of `seq<Records>`(default), `seq<Tuples>`,`DataTable`, or raw `SqlReader` for custom parsing. 
        For `seq<Records>` and `seq<Tuples>` each column mapped to corresponding property/item
    * Nullable output columns translate to the F# Option type
    * Output parameters and return values for stored procedures are not supported. Open a GitHub project issue to request this support.
* Extra configuration options:
    * `SingleRow` hint forces singleton output instead of sequence

* [Microsoft.SqlServer.Types (Spatial on Azure)](http://blogs.msdn.com/b/adonet/archive/2013/12/09/microsoft-sqlserver-types-nuget-package-spatial-on-azure.aspx) is supported.
* SqlCommandProvider and SqlProgrammabilityProvider are of "erased types" kind. It can be used only from F#. 
* SqlEnumProvider is of "generated types" kind and can be used from any .NET language.

Limitations
-------------------------------------
In addition to system requirements listed above `SqlCommandProvider and SqlProgrammabilityProvider` constrained by same limitations as two system meta-stored procedures 
it uses in implementation: [sys.sp\_describe\_undeclared\_parameters](http://technet.microsoft.com/en-us/library/ff878260.aspx) 
and [sys.sp\_describe\_first\_result\_set](http://technet.microsoft.com/en-us/library/ff878602.aspx). Look online for more details.
*)

