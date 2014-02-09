(*** hide ***)
#r "../../bin/FSharp.Data.Experimental.SqlCommandProvider.dll"

(**

Configuration
===================

Provider parameters 
-------------------------------------

<table class="table table-bordered table-striped">
<thead><tr><td>Name</td><td>Default</td><td>Accepted values</td></tr></thead>
<tbody>
  <tr><td class="title">CommandText</td><td>-</td><td>T-SQL script or *.sql file</td></tr></thead>
  <tr><td class="title">ConnectionStringOrName</td><td>-</td><td>Connection string or name</td></tr></thead>
  <tr><td class="title">CommandType</td><td>CommandType.Text</td><td>Text or StoredProcedure</td></tr></thead>
  <tr><td class="title">ResultType</td><td>ResultType.Tuples</td><td>Tuples, Records, DataTable or Maps</td></tr></thead>
  <tr><td class="title">SingleRow</td><td>false</td><td>true/false</td></tr></thead>
  <tr><td class="title">ConfigFile</td><td>app.config or web.config</td><td>valid file name</td></tr></thead>
  <tr><td class="title">AllParametersOptional</td><td>false</td><td>true/false</td></tr></thead>
  <tr><td class="title">DataDirectory</td><td>""</td><td>valid file system path</td></tr></thead>
</tbody>
</table>

CommandText
-------------------------------------

### T-SQL script
*)

open FSharp.Data.Experimental

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

//Inline T-SQL text convinient for short queries 
type GetDate = SqlCommand<"SELECT GETDATE() AS Now", connectionString>

//More complex queries are better off extracted to stand-alone literals

//Fibonacci! Not again! :)
[<Literal>]
let fibonacci = "
    WITH Fibonacci ([N-1], N) AS
    ( 
        --seed
	    SELECT CAST(0 AS BIGINT), CAST(1 AS BIGINT)

	    UNION ALL
        --fold
	    SELECT N, [N-1] + N
	    FROM Fibonacci
    )

    SELECT TOP(@Top) [N-1] 
    FROM Fibonacci
"

type FibonacciQuery = SqlCommand<fibonacci, connectionString>

FibonacciQuery()
    .Execute(10L) 
    |> Seq.map Option.get 
    |> Seq.toArray 
    |> printfn "First 10 fibonacci numbers: %A" 


(**
### External *.sql file

An ability to use external \*.sql file instead of inline strings can improve developement expirience.
Visual Studio has rich tooling support for *.sql files. (via SQL Server Data Tools) 

<img src="img/sql_file_As_CommandText_1.png"/>

<img src="img/sql_file_As_CommandText_2.png"/>


It has the following benefits:

  * Intellisense in both F# and T-SQL code (it cannot get better)
  * T-SQL syntax highlighting and verification
  * Testing: query execution gives immediate feedback (small trick required. see the picture above)
  * Clean separation between T-SQL and F# code

Having all data access layer logic in bunch of files in one location has clear advantage. 
For example, it can be handed over to DBA team for optimization. It's harder to do when applicatoin and data access
mixed together (LINQ).

Extrating T-SQL into external files is not the only way to scale application development. 
The other alternative is to push logic into programmable objects. 
I strongly recommend T-SQL functions because they have typical benefits of functional-first
programming style: composition (therefore reuse), restricted side-effects and simple substitution model (easy to reason about).
Stored procedures can be used too but they resemble imperative programming with all drawbacks attached.

Below is example of SQL Table-Valued Function usage. 
*)

type GetContactInformation = SqlCommand<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString>

(**
### Syntax erros

In case of syntax errors in T-SQL the type provider shows fairly clear error message.

### Limitation: a single parameter in a query may only be used once. 

For example attempt to use following query will fail:
<div class="row">	
SELECT </br>
	CASE </br>
	    WHEN @x % 3 = 0 AND @x % 5 = 0 THEN 'FizzBuzz' </br>
	    WHEN @x % 3 = 0 THEN 'Fizz' </br>
	    WHEN @x % 5 = 0 THEN 'Buzz' </br>
	    ELSE CAST(@x AS NVARCHAR) </br>
	END </br>
</div>	

You can work around this by declaring a local intermediate variable in t-sql script and assigning a paramater in question to that variable.
*)
    
type FizzOrBuzz = SqlCommand<"
    DECLARE @x AS INT = @xVal
    SELECT 
	    CASE 
		    WHEN @x % 3 = 0 AND @x % 5 = 0 THEN 'FizzBuzz' 
		    WHEN @x % 3 = 0 THEN 'Fizz' 
		    WHEN @x % 5 = 0 THEN 'Buzz' 
		    ELSE CONCAT(@x, '') --use concat to avoid nullable column
	    END", connectionString>

let fizzOrBuzz = FizzOrBuzz()
printfn "Answer on interview:\n%A" [ for i = 1 to 100 do yield! fizzOrBuzz.Execute(i) ]

(**

ConnectionStringOrName 
-------------------------------------

### Inline or literal   

Connection string can be provided either via literal (all examples above) or inline 
*)

//Inline 
type Get42 = SqlCommand<"SELECT 42", @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True">

(**

The literal version is more practical because connection string definition can be shared between different declarations of SqlCommand<...>.

### By name

The other option is to supply connection string name from config file. 
*)

//default config file name is app.config or web.config
type Get43 = SqlCommand<"SELECT 43", "name=AdventureWorks2012">

//specify ANY other file name (including web.config) explicitly
type Get44 = SqlCommand<"SELECT 44", "name=AdventureWorks2012", ConfigFile = "user.config">

(**
I would like to emphasize that _ConfigFile_ is about ***design time only*. 
Let me give you couple examples to clarify:

  * You build Windows Service or WPF application. 
    </br></br>
    <img src="img/ConnStrByNameAppConfig.png"/>
    </br></br> 
    Assuming it's purely F# project and default app.config there _ConfigFile_ parameter can be omitted. 
    Still during runtime connection string with the same name should be available via .NET configuration infrastructure (ConfigurationManager)
    It means that either you have packaging/deplyment system that knows to fix connection string in config file to point to 
    production database or you do it manually after application deployed.

  * You have mixed ASP.NET WebAPI solution: C# hosting project and F# contollers implementation project.
    </br></br>
    <img src="img/ConnStrByNameUserConfig.png"/>
    </br></br> 
    F# controllers project is simple library project. It has data access layer module.
    `SqlCommand<...>` defintions refer to connection string by name form user.config file.  
    </br></br>
    <img src="img/ConnStrByNameUserConfig2.png"/>
    </br></br> 
    It's completely legitimate not to check-in this user.config file into source control system if it's developer specific.  
    Similar setup can be applied inside single project to separate user specific configuration from common production config.

### Overriding connection string at run-time

  Run-time database connectivity configuration is rarely (almost never) the same as design-time. 
  All SqlCommand<...> generated types can be re-configured at run-time via optional constructor paramater.
  The parameter is optional because config file + name approach is acceptable way to have run-time configuration different from design-time.

Input
===================
*)

