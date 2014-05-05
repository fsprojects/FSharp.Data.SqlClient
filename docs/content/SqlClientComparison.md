Unleashing the power of F# Type Providers in database world
==============================================

Type providers and Query Expressions - great together
-----------------------------------------

 LINQ and [Query Expressions][query] (aka LINQ 2.0) introduced in F# 3.0 is a lovely and useful technology. 
 It makes writing data access code a pleasant exercise. It is built on top of database Type Providers exposing structure and data in F# code.
 
 There are several different implementations of those available: 
 
 * [SQLProvider][sql] - recent F# community effort, based on direct ADO.NET
 * [SqlDataConnection][linq2sql]/[DbmlFile][dbml]  - based on Linq2Sql framework
 * [SqlEntityConnection][ef]/[EdmxFile][edmx] - based on Entity Framework.  
 
However, here I would like to point out some of the reasons why an alternative approach to query processing 
offered by [FSharp.Data.SqlClient][sqlClient] is a better solution to the woes of .Net developers (at least, in some cases). 

StackOverflow has hundreds of issues like [this one][soissue] for all kinds of ORM frameworks from NHibernate to 
Entity Framework. Perfectly valid code fails in run-time because of unsupported F#-to-SQL (or C#-to-SQL) translation 
semantics, a great example of leaky abstraction. Lack of control and opaqueness of F#-to-SQL conversion spell 
performance problems as well, like infamous N+1 issue.  
Readers are no doubt familiar with a popular blog post [The Vietnam of Computer Science][vietnam] by Ted Neward going deep 
into so-called [object-relational impedance mismatch][orm], which is at the core of these issues.

So far the industry answer to this was a number of so-called micro-ORMs with a mission of making conversion 
from database types to .Net objects as simple as possible while refraining from making any assumptions 
about the actual mapping.

What all of them lack, however, is an ability to verify correctness of SQL queries in compile-time. 

And that's where SqlCommandProvider, the core element of [FSharp.Data.SqlClient][sqlClient] Type Provider, really shines. 
Essentially, it offers "What You See Is What You Get" for SQL queries. Once F# code involving SqlCommandProvider 
passes compilation stage you are guaranteed to have valid executable (both F# code and T-SQL).

Here is a typical snippet of SqlCommandProvider-enabled code: 

	open FSharp.Data

	[<Literal>]
	let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

	[<Literal>]
	let query = "
		SELECT TOP(@TopN) FirstName, LastName, SalesYTD 
		FROM Sales.vSalesPerson
		WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
		ORDER BY SalesYTD
	" 

	type SalesPersonQuery = SqlCommandProvider<query, connectionString>
	let cmd = new SalesPersonQuery()

	cmd.AsyncExecute(TopN = 3L, regionName = "United States", salesMoreThan = 1000000M) 
	|> Async.RunSynchronously

	//output
	seq
		[("Pamela", "Ansman-Wolfe", 1352577.1325M);
		 ("David", "Campbell", 1573012.9383M);
		 ("Tete", "Mensa-Annan", 1576562.1966M)]

Now, if database schema changes or there is a typo anywhere in the query, F# compiler notifies developer immediately:

<img src="img/error_in_query.png"/>

The secret is that SqlCommandProvider uses features available in MS SQL Server 2012 and SQL Azure to compile SQL query and infer 
input parameters and output schema in compile time. Please see [SqlClient Type Provider website][sqlClient] for more details.

   


Comparison with the best of breed - [Dapper][dapper]
-----------------------------------------------------------------------------------------

Dapper is a micro-ORM by StackOverflow with a main goal of being extremely fast. Here is a [description][dapperInfo] from StackOverflow itself:

>dapper is a micro-ORM, offering core parameterization and materialization services, but (by design) not the full breadth of services that you might 
expect in a full ORM such as LINQ-to-SQL or Entity Framework. Instead, it focuses on making the materialization as fast as possible, with no overheads 
from things like identity managers - just "run this query and give me the (typed) data".

### Performance

Dapper comes with an excellent [benchmark][benchmark]. The focus of the test is on deserialization. 
Here is how FSHarp.Data.SqlClient compares with all major .Net ORMs:

<img src="img/dapper.png"/>

Tests were executed against SqlExpress 2012 so the numbers are a bit higher than what you can 
see on [Dapper page][benchmarkDapper]. A test retrieves single record with 13 properties by random id 500 times and deserializes it. Trials for different 
ORMs are mixed up randomly. All executions are synchronous.

Note that we didn't put any specific effort into improving FSharp.Data.SqlClient performance for this test. The very nature of type providers helps to produce
the simplest run-time implementation and hence be as close as possible to hand-coded ADO.NET code.

### Usage

Keeping in mind that FSharp.Data.SqlClient is not strictly an ORM in commonly understood sense of the term, here are the some pros and cons:

* Because result types are auto-generated, FSharp.Data.SqlClient doesn't support so-called [multi-mapping][multi-mapping]
* As FSharp.Data.SqlClient is based on features specific for MS SQL Server 2012, Dapper provides much wider range of supported scenarios
* Other side of this is that FSharp.Data.SqlClient fully supports SqlServer-specific types like [hierarchyId][hierarchyId] and 
[spatial types][spatial], which Dapper has no support for
* FSharp.Data.SqlClient fully supports User-Defined Table Types for input parameters with no additional coding required, 
[as opposed to Dapper][soissue2]
* Dapper intentionally  has no support for `SqlConnection` management; FSharp.Data.SqlClient encapsulates `SqlConnection` 
life-cycle including asynchronous scenarios while optionally accepting external `SqlTransaction`.

Following FSharp.Data.SqlClient features are unique:

* Reasonable auto-generated result type definition so there is no need to define it in code
* Sql command is just a string for other ORMs, while FSHarp.Data.SqlClient verifies it and figures out input parameters and output types 
so design-time experience is simply unparalleled
* Design-time verification means less run-time tests, less yak shaving synchronizing database schema with code definitions, and earliest possible 
identification of bugs and mismatches
* `SqlProgrammabilityProvider` lets user to explore stored procedures and user-defined functions right from the code with IntelliSense

Conclusion
------------------------

F# 3.0 Type Providers dramatically improve developer experience exposing data with IntelliSense in design time.
Combined with the latest features of MS SQL Server, FSharp.Data.SqlClient Type Provider empowers users to write compile time-verified 
F# and SQL code leaving no space for boilerplate while promising performance comparable with the best-of-breed traditional solutions.

[dapper]: https://code.google.com/p/dapper-dot-net/
[dapperInfo]: http://stackoverflow.com/tags/dapper/info
[benchmark]: https://code.google.com/p/dapper-dot-net/source/browse/Tests/PerformanceTests.cs
[benchmarkDapper]: https://github.com/SamSaffron/dapper-dot-net#performance-of-select-mapping-over-500-iterations---poco-serialization
[multi-mapping]: http://stackoverflow.com/a/6001902/862313
[hierarchyId]: http://technet.microsoft.com/en-us/library/bb677173.aspx
[spatial]: http://blogs.msdn.com/b/adonet/archive/2013/12/09/microsoft-sqlserver-types-nuget-package-spatial-on-azure.aspx
[soissue2]: http://stackoverflow.com/questions/6232978/does-dapper-support-sql-2008-table-valued-parameters
[ds]: http://msdn.microsoft.com/en-us/library/wha85tzb.aspx
[sqlClient]: http://fsprojects.github.io/FSharp.Data.SqlClient/
[sql]: http://github.com/fsprojects/SQLProvider
[query]: http://msdn.microsoft.com/en-us/library/hh225374.aspx
[linq2sql]: http://msdn.microsoft.com/en-us/library/hh361033.aspx
[dbml]: http://msdn.microsoft.com/en-us/library/hh361039.aspx
[ef]: http://msdn.microsoft.com/en-us/library/hh361035.aspx
[edmx]: http://msdn.microsoft.com/en-us/library/hh361038.aspx
[soissue]: http://stackoverflow.com/questions/21574254/how-do-i-do-a-contains-query-with-f-query-expressions/21584169
[vietnam]: http://blogs.tedneward.com/2006/06/26/The+Vietnam+Of+Computer+Science.aspx
[orm]:http://en.wikipedia.org/wiki/Object-relational_impedance_mismatch