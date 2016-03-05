(*** hide ***)
#r "Microsoft.SqlServer.Types.dll"
#r @"..\..\bin\FSharp.Data.SqlClient.dll"   

open FSharp.Data

[<Literal>]
let connectionString = 
    @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

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
        ">()
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
    use conn = new System.Data.SqlClient.SqlConnection( connectionString)
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

<div class="well well-small" style="margin:0px 70px 0px 20px;">

**Note** Unfortunate downside of this amazing feature is absent of intellisense for 
both static method parameters and actual method parameters. This is compiler/tooling issue and tracked here:

https://github.com/Microsoft/visualfsharp/issues/642 <br/>
https://github.com/Microsoft/visualfsharp/pull/705 <br/>
https://github.com/Microsoft/visualfsharp/issues/640 <br/>

Please help to improve quality of F# compiler and tooling by providing feedback to [F# team](https://twitter.com/VisualFSharp) 
or [Don Syme](https://twitter.com/VisualFSharp). 

</p></div>
*)
