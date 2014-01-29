(*** hide ***)
#r "../../bin/FSharp.Data.Experimental.SqlCommandProvider.dll"

(**
Bridging the gap between T-SQL queries and F# type system
===================

SqlCommandProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way.

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The FSharp.Data.Experimental.SqlCommandProvider library can be <a href="http://www.nuget.org/packages/FSharp.Data.Experimental.SqlCommandProvider">installed from NuGet</a>:
      <pre>PM> Install-Package FSharp.Data.Experimental.SqlCommandProvider</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Sample code 
-------------------------------------

The query below retrieves top 3 sales representatives from North American region who have sales YTD of more than one million. 

*)

open FSharp.Data.Experimental

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>]
let query = "
    SELECT TOP(@TopN) FirstName, LastName, SalesYTD 
    FROM Sales.vSalesPerson
    WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
    ORDER BY SalesYTD
" 

type SalesPersonQuery = SqlCommand<query, connectionString>
let cmd = SalesPersonQuery()

cmd.AsyncExecute(TopN = 3L, regionName = "United States", salesMoreThan = 1000000M) 
|> Async.RunSynchronously

//output
seq
    [("Pamela", "Ansman-Wolfe", 1352577.1325M);
     ("David", "Campbell", 1573012.9383M);
     ("Tete", "Mensa-Annan", 1576562.1966M)]

(**

System requirements
-------------------------------------

 * SQL Server 2012 and up or SQL Azure Database at compile-time. 
 * .NET 4.0 or higher

Features at glance:
-------------------------------------

* Static type per SqlCommand<...> with 2 methods:
    * AsyncExecute - for scalability scenarios 
    * Execute - convenience when needed
* Configuration
    * Command text (sql script) provided either as literal or path to *.sql file
    * Connection string is either literal or from config file
    * Connection string can be overridden at run time via constructor  
* Statically typed input/output:
    * Unbound sql variables/input parameters mapped to mandatory arguments for AsyncExecute/Execute
    * Inferred type for output. Choice of `seq<Tuples>`, `seq<Records>`, `DataTable` or `seq<Maps>`. Each column mapped to item/property/key.
* Extra configuration options:
    * SingleRow hint forces singleton output instead of sequence
    * Set AllParametersOptional to true to make all parameters optional (nullable).
* Set CommandType parameter to CommandType.StoredProcedure to specify it directly by name. 
*)

