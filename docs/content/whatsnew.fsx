(*** hide ***)
#r "Microsoft.SqlServer.Types.dll"
#r @"..\..\bin\net40\FSharp.Data.SqlClient.dll"   

open FSharp.Data

[<Literal>]
let connectionString = 
    @"Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type DB = SqlProgrammabilityProvider<connectionString>

(**

Inline T-SQL with SqlProgrammabilityProvider
-------------------------------------

Starting version 1.8.1 SqlProgrammabilityProvider leverages 
[a new F# 4.0 feature](https://github.com/fsharp/FSharpLangDesign/blob/master/FSharp-4.0/StaticMethodArgumentsDesignAndSpec.md) 
to support inline T-SQL. 

*)
do
    use cmd = 
        DB.CreateCommand<"
            SELECT TOP(@topN) FirstName, LastName, SalesYTD 
            FROM Sales.vSalesPerson
            WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
            ORDER BY SalesYTD
        ">(connectionString)
    cmd.Execute(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) |> printfn "%A"

(**
This makes `SqlProgrammabilityProvider` as one stop shop for both executing inline T-SQL statements 
and accessing to built-in objects like stored procedures, functoins and tables. 
Connectivity information (connection string and/or config file name) is defined in one place 
and doesn't have be carried around like in SqlCommandProvider case.

`CreateCommand` optionally accepts connection, transaction and command timeout parameters. 
Any of these parameters can be ommited.  
*)

#r "System.Transactions"

do
    use conn = new Microsoft.Data.SqlClient.SqlConnection( connectionString)
    conn.Open()
    use tran = conn.BeginTransaction()
    use cmd = 
        DB.CreateCommand<"INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())">(
            connection = conn, 
            transaction = tran, 
            commandTimeout = 120
        )

    cmd.Execute( Code = "BTC", Name = "Bitcoin") |> printfn "Records affected %i"
    //Rollback by default. Uncomment a line below to commit the change.
    //tran.Commit()

(**

Access to command and record types
-------------------------------------

`CreateMethod` combines command type definition and constructor invocation. 
Compare it with usage of `SqlCommandProvider` where generated command type aliased explicitly.
*)

let cmd1 = DB.CreateCommand<"SELECT name, create_date FROM sys.databases">(connectionString)
// vs
type Get42 = SqlCommandProvider<"SELECT name, create_date FROM sys.databases", connectionString>
let cmd2 = new Get42(connectionString)

//access to Record type
type Cmd2Record = Get42.Record

(** 
Although #3 is most verbose it has distinct advantage of providing straightforward access 
to type of generated command and record. 
This becomes important for [unit testing] or explicit type annotations scenarios. 
By default CreateCommand usage triggers type generation as well. 
A type located under Commands nested type. 
*)

type Cmd1Command = 
    DB.Commands.``CreateCommand,CommandText"SELECT name, create_date FROM sys.databases"``

type Cmd1Record = Cmd1Command.Record

(**
Type names are generated by compiler and look ugly. 
The type provider knows to remove illegal '=' and '@' characters 
but auto-competition still chokes on multi-line definitions. 

A workaround is to provide explicit name for generated command type
*)

let cmd3 = DB.CreateCommand<"SELECT name, create_date FROM sys.databases", TypeName = "Get42">(connectionString)
type Cmd3Record = DB.Commands.Get42.Record

(**
<div class="well well-small" style="margin:0px 70px 0px 20px;">

**Note** Unfortunate downside of this amazing feature is absent of intellisense for 
both static method parameters and actual method parameters. This is compiler/tooling issue and tracked here:

https://github.com/Microsoft/visualfsharp/issues/642 <br/>
https://github.com/Microsoft/visualfsharp/pull/705 <br/>
https://github.com/Microsoft/visualfsharp/issues/640 <br/>

Please help to improve quality of F# compiler and tooling by providing feedback to [F# team](https://twitter.com/VisualFSharp) 
or [Don Syme](https://twitter.com/dsyme). 

</p></div>
*)
