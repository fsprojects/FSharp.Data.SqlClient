(*** hide ***)
#r @"..\..\src\SqlClient\bin\Debug\FSharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types.dll"
(**

Configuration and Input
===================

SqlCommandProvider parameters 
-------------------------------------

<table class="table table-bordered table-striped">
<thead><tr><td>Name</td><td>Default</td><td>Accepted values</td></tr></thead>
<tbody>
  <tr><td class="title">CommandText</td><td>-</td><td>T-SQL script or *.sql file</td></tr>
  <tr><td class="title">ConnectionStringOrName</td><td>-</td><td>Connection string or name</td></tr>
  <tr><td class="title">ResultType</td><td>ResultType.Records</td><td>Tuples, Records, DataTable, or DataReader</td></tr>
  <tr><td class="title">SingleRow</td><td>false</td><td>true/false</td></tr>
  <tr><td class="title">ConfigFile</td><td>app.config or web.config</td><td>Valid file name</td></tr>
  <tr><td class="title">AllParametersOptional</td><td>false</td><td>true/false</td></tr>
  <tr><td class="title">ResolutionFolder</td><td>The folder that contains the project or script.</td><td>Valid file system path. Absolute or relative.</td></tr>
</tbody>
</table>

SqlProgrammabilityProvider parameters 
-------------------------------------

<table class="table table-bordered table-striped">
<thead><tr><td>Name</td><td>Default</td><td>Accepted values</td></tr></thead>
<tbody>
  <tr><td class="title">ConnectionStringOrName</td><td>-</td><td>Connection string or name</td></tr>
  <tr><td class="title">ResultType</td><td>ResultType.Records</td><td>Tuples, Records, DataTable, or DataReader</td></tr>
  <tr><td class="title">ConfigFile</td><td>app.config or web.config</td><td>valid file name</td></tr>
  <tr><td class="title">ResolutionFolder</td><td>The folder that contains the project or script.</td><td>Valid file system path. Absolute or relative.</td></tr>
</tbody>
</table>

CommandText
-------------------------------------

### T-SQL script
*)

open FSharp.Data

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

//Inline T-SQL text convinient for short queries 
type GetDate = SqlCommandProvider<"SELECT GETDATE() AS Now", connectionString>

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

type FibonacciQuery = SqlCommandProvider<fibonacci, connectionString>

(new FibonacciQuery())
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


It offers following benefits:

  * Intellisense in both F# and T-SQL code (it cannot get better)
  * T-SQL syntax highlighting and verification
  * Testing: query execution gives immediate feedback (small trick required - see the picture above)
  * Clean separation between T-SQL and F# code

Having all data access layer logic in bunch of files in one location has clear advantage. 
For example, it can be handed over to DBA team for optimization. It's harder to do when application and data access
are mixed together (LINQ).
*)

type CommandFromFile = SqlCommandProvider<"GetDate.sql", connectionString>
let cmd = new CommandFromFile()
cmd.Execute() |> ignore

(**

Extracting T-SQL into external files is not the only way to scale application development. 
The other alternative is to push logic into programmable objects. 
I strongly recommend T-SQL functions because they have typical benefits of functional-first
programming style: composition (therefore reuse), restricted side-effects and simple substitution model (easy to reason about).
Stored procedures can be used too but they resemble imperative programming with all the drawbacks attached.

Below is an example of SQL Table-Valued Function usage. 
*)

type GetContactInformation = 
    SqlCommandProvider<"SELECT * FROM dbo.ufnGetContactInformation(@PersonId)", connectionString>

(**
### Syntax errors

The type provider shows fairly clear error message if there are any syntax errors in T-SQL. 
An instantaneous feedback is one of the most handy features of `SqlCommandProvider`. 

### Limitation: a single parameter in a query may only be used once. 

For example, an attempt to use following query will fail:

    [lang=sql]
    WHEN @x % 3 = 0 AND @x % 5 = 0 THEN 'FizzBuzz' 
    WHEN @x % 3 = 0 THEN 'Fizz' 
    WHEN @x % 5 = 0 THEN 'Buzz' 
    ELSE CAST(@x AS NVARCHAR) 

You can work around this by declaring a local intermediate variable in t-sql script and assigning a parameter in question to that variable.
*)
    
type FizzOrBuzz = SqlCommandProvider<"
    DECLARE @x AS INT = @xVal
    SELECT 
	    CASE 
		    WHEN @x % 3 = 0 AND @x % 5 = 0 THEN 'FizzBuzz' 
		    WHEN @x % 3 = 0 THEN 'Fizz' 
		    WHEN @x % 5 = 0 THEN 'Buzz' 
		    ELSE CONCAT(@x, '') --use concat to avoid nullable column
	    END", connectionString>

let fizzOrBuzz = new FizzOrBuzz()
printfn "Answer on interview:\n%A" [ for i = 1 to 100 do yield! fizzOrBuzz.Execute(i) ]

(**

ConnectionStringOrName 
-------------------------------------

### Inline or literal   

Connection string can be provided either via literal (all examples above) or inline 
*)

//Inline 
type Get42 = 
    SqlCommandProvider<"SELECT 42", 
                       @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True">

(**

The literal version is more practical because connection string definition can be shared between different declarations of `SqlCommandProvider<...>`.

### By name

The other option is to supply connection string name from config file. 
*)

//default config file name is app.config or web.config
type Get43 = SqlCommandProvider<"SELECT 43", "name=AdventureWorks2012">

//specify ANY other file name (including web.config) explicitly
type Get44 = SqlCommandProvider<"SELECT 44", "name=AdventureWorks2012", ConfigFile = "user.config">

(**
I would like to emphasize that `ConfigFile` is about ***design time only*. 
Let me give you couple examples to clarify:

  * You build Windows Service or WPF application. 
    </br></br>
    <img src="img/ConnStrByNameAppConfig.png"/>
    </br></br> 
    If it is a purely F# project and default app.config is there `ConfigFile` parameter can be omitted. 
    Still, in runtime the connection string with the same name should be available via .NET configuration infrastructure (ConfigurationManager).
    It means that either you have packaging/deployment system that knows how to fix connection string in config file to point to 
    production database (for example, [Slow Cheetah](http://visualstudiogallery.msdn.microsoft.com/69023d00-a4f9-4a34-a6cd-7e854ba318b5)),
    or you do it manually after application is deployed.

  * You have mixed ASP.NET WebAPI solution: C# hosting project and F# controllers implementation project.
    </br></br>
    <img src="img/ConnStrByNameUserConfig.png"/>
    </br></br> 
    F# controllers project is a simple library project. It has data access layer module.
    `SqlCommandProvider<...>` definitions refer to connection string by name form user.config file.  
    </br></br>
    <img src="img/ConnStrByNameUserConfig2.png"/>
    </br></br> 
    It's completely legitimate not to check-in this user.config file into source control system if it's developer specific.  
    Similar setup can be applied inside single project to separate user-specific configuration from common production config.

### Overriding connection string at run-time

Run-time database connectivity configuration is rarely (almost never) the same as design-time. 
All `SqlCommandProvider<_>`-generated types can be re-configured at run-time via optional constructor parameter.
The parameter is optional because "config file + name" approach is an acceptable way to have run-time configuration different from design-time.
Several use cases are possible:
*)

//Case 1: pass run-time connection string into ctor
let runTimeConnStr = "..." //somehow get connection string at run-time
let get42 = new Get42(runTimeConnStr)

//Case 2: bunch of command types, single database
//Factory or IOC of choice to avoid logic duplication. Use F# ctor static constraints.
module DB = 
    [<Literal>]
    let connStr = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

    type MyCmd1 = SqlCommandProvider<"SELECT 42", connStr>
    type MyCmd2 = SqlCommandProvider<"SELECT 42", connStr>

    let inline createCommand() : 'a = 
        let connStr = "..." //somehow get connection string at run-time
        //invoke ctor
        (^a : (new : string * int -> ^a) (connStr, 30)) 
        //or
        //(^a : (static member Create: string * int -> ^a) (connStr, 30)) 

let dbCmd1: DB.MyCmd1 = DB.createCommand()
let dbCmd2: DB.MyCmd2 = DB.createCommand()

//Case 3: multiple databases
//It gets tricky because we need to distinguish between command types associated with different databases. 
//Static type property ConnectionStringOrName that has exactly same value as passed into SqlCommandProvider helps.
module DataAccess = 
    [<Literal>]
    let adventureWorks = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
    [<Literal>]
    let master = @"Data Source=(LocalDb)\v11.0;Initial Catalog=master;Integrated Security=True"

    type MyCmd1 = SqlCommandProvider<"SELECT 42", adventureWorks>
    type MyCmd2 = SqlCommandProvider<"SELECT 42", master>

    let inline createCommand() : 'a = 
        let designTimeConnectionString = (^a : (static member get_ConnectionStringOrName : unit -> string) ())
        let connStr = 
            if designTimeConnectionString = adventureWorks  
            then "..." //somehow get AdventureWorks connection string at run-time
            elif designTimeConnectionString = master
            then "..." //somehow get master connection string at run-time
            else failwith "Unexpected"
        //invoke ctor
        (^a : (new : string -> ^a) connStr) 

let adventureWorksCmd: DataAccess.MyCmd1 = DataAccess.createCommand()
let masterCmd: DataAccess.MyCmd2 = DataAccess.createCommand()
(**

Another related case, albeit not that common, is local transaction.

**Important:** `SqlConnection` associated with passed transaction, is not closed automatically in this case. 
It is responsibility of the client code to close and dispose it.
*)
[<Literal>]
let bitCoinCode = "BTC"
[<Literal>]
let bitCoinName = "Bitcoin"

type DeleteBitCoin = 
    SqlCommandProvider<"DELETE FROM Sales.Currency WHERE CurrencyCode = @Code"
                        , connectionString>
type InsertBitCoin = 
    SqlCommandProvider<"INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())"
                        , connectionString>
type GetBitCoin = 
    SqlCommandProvider<"SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code"
                        , connectionString>

(new DeleteBitCoin()).Execute(bitCoinCode) |> ignore
let conn = new System.Data.SqlClient.SqlConnection(connectionString)
conn.Open()
let tran = conn.BeginTransaction()
(new InsertBitCoin(tran)).Execute(bitCoinCode, bitCoinName) = 1
((new GetBitCoin(tran)).Execute(bitCoinCode) |> Seq.length) = 1
tran.Rollback()
((new GetBitCoin(tran)).Execute(bitCoinCode) |> Seq.length) = 0

(**

It is worth noting that because of "erased types" nature of this type provider reflection and other dynamic techniques cannot be used 
to create command instances.

`SqlProgrammabilityProvider<...>` supports connection name syntax as well. 
It is also possible to pass run-time connection string to database constructor:
*)
type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>

open System

let dbRuntime = AdventureWorks2012(runTimeConnStr)

dbRuntime.``Stored Procedures``.``dbo.uspGetWhereUsedProductID``.AsyncExecute(DateTime(2013,1,1), 1) 
|> Async.RunSynchronously

(**  
### Stored procedures

  Stored procedures are supported by `SqlProgrammabilityProvider<...>`
*)

let db = AdventureWorks2012()

db.``Stored Procedures``.``dbo.uspGetWhereUsedProductID``.AsyncExecute(DateTime(2013,1,1), 1) 
|> Async.RunSynchronously 
|> Array.ofSeq

(**
   If Stored Procedure contains any output parameters, the result of the call is a record with all output parameters and return value. 
   Note that SqlServer-specific types are also supported.
*)
open Microsoft.SqlServer.Types
open System.Data

let res = db.``Stored Procedures``.``HumanResources.uspUpdateEmployeeLogin``
            .AsyncExecute(  291, 
                            true, 
                            DateTime(2013,1,1), 
                            "gatekeeper", 
                            "adventure-works\gat0", 
                            SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/")))
            |> Async.RunSynchronously 
res.ReturnValue

(**

### Optional input parameters
    
By default all input parameters of `AsyncExecute/Execute` generated by `SqlCommandProvider<...>` are mandatory. 
But there are rare cases when you prefer to handle NULL input values inside T-SQL script. 
`AllParametersOptional` set to true makes all parameters (guess what) optional.
*)

type IncrBy = SqlCommandProvider<"SELECT @x + ISNULL(CAST(@y AS INT), 1) ", 
                                    connectionString, 
                                    AllParametersOptional = true, 
                                    SingleRow = true>
let incrBy = new IncrBy()
//pass both params passed 
incrBy.Execute(Some 10, Some 2) = Some( Some 12) //true
//omit second parameter. default to 1
incrBy.Execute(Some 10) = Some( Some 11) //true

(**
Note that `AllParametersOptional` is not supported by `SQlProgrammabilityProvider<...>` as it is able to 
infer default values for Stored Procedures and UDFs so `AsyncExecute` signature makes corresponding parameters optional.

### Table-valued parameters (TVPs)

Sql command needs to call a stored procedure or user-defined function that takes a parameter of table-valued type. 

Set up sample type and sproc:

<pre>
<code>
CREATE TYPE myTableType AS TABLE (myId int not null, myName nvarchar(30) null) 
GO 
CREATE PROCEDURE myProc 
   @p1 myTableType readonly 
AS 
BEGIN 
   SELECT myName from @p1 p 
END 
</code>
</pre>
*)

type TableValuedSample = SqlCommandProvider<"exec myProc @x", connectionString>
type TVP = TableValuedSample.myTableType
let tvpSp = new TableValuedSample()
//nullable columns mapped to optional ctor params
tvpSp.Execute(x = [ TVP(myId = 1, myName = Some "monkey"); TVP(myId = 2) ]) 

(**
Same with `SqlProgrammabilityProvider<...>`
*)

type myType = AdventureWorks2012.``User-Defined Table Types``.MyTableType

let m = [ 
    myType(myId = 2)
    myType(myId = 1) 
]

let myArray = 
    db.``Stored Procedures``.``dbo.MyProc``.AsyncExecute(m) 
    |> Async.RunSynchronously 
    |> Array.ofSeq

let myRes = myArray.[0]
myRes.myId
myRes.myName