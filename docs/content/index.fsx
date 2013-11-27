(*** hide ***)
#r "../../src/SqlCommandTypeProvider/bin/Debug/SqlCommandTypeProvider.dll"

(**
Bridging the gap between T-SQL queries and F# type system.
===================

SqlCommandTypeProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way.

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The F# ProjectTemplate library can be <a href="https://www.nuget.org/packages/SqlCommandTypeProvider">installed from NuGet</a>:
      <pre>PM> Install-Package SqlCommandTypeProvider</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Sample code 
-------------------------------------

The query below retrieves top 3 sales representative from North American region who has sales YTD for more than one million. 

*)

open FSharp.Data.SqlClient

[<Literal>]
let connectionString = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

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

Notice how T-SQL unbound variables mapped into parameters of AsyncExecute method. Output is a sequence of tuples. 
Tuple elemens match to the columns of query result set.

System requirements
-------------------------------------

 * SQL Server 2012 and up or SQL Azure Database at compile-time. 
 * .NET 4.0 or higher
*)

