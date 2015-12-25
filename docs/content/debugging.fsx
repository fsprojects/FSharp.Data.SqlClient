(*** hide ***)
#r @"..\..\bin\FSharp.Data.SqlClient.dll"
#r "System.Transactions"
open FSharp.Data
open System

[<Literal>]
let connectionString = @"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True"

(**

Debugging
===================

Following FSharp.Data.SqlClient specific techniques can be used to diagnose various issues.  

ToTraceString
-------------------------------------
Call ToTraceString to get text representation of sql statement that will be executed in Sql Server   

*)

let cmd = new SqlCommandProvider<"
    SELECT TOP(@topN) FirstName, LastName, SalesYTD 
    FROM Sales.vSalesPerson
    WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
    ORDER BY SalesYTD
    " , connectionString>()

cmd.ToTraceString(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) 
|> printfn "Sql: %s"

(**

Type providers 

Direct access to underlying SqlCommand instance. 
-------------------------------------
If you feel that getting your hands on underlying ADO.NET SqlCommand can help to address a problem that can be done. 
Expect to see a warning because this is not intended for public use and subject for change. 
Avoid tempering state of this SqlCommand instance – otherwise all bets are off.
*)

let adonetCmd = (cmd :> ISqlCommand).Raw

[ for p in adonetCmd.Parameters -> p.ParameterName, p.SqlDbType ]
|> printfn "Inferred parameters: %A" 

(**

Result set runtime verification. 
-------------------------------------
While enjoying all benefits of static types at design time one can easily end up in a situation 
when runtime Sql Server database schema is different from compile time. 
Up until now this resulted in confusion runtime exception: InvalidCastException("Specified cast is not valid."). 

To improve diagnostics without hurting performance a new configuration section/switch is introduced. 

First, define custom sectoin in app.config/web.config
    <configSections>
        <section name="FSharp.Data.SqlClient" type="System.Configuration.NameValueSectionHandler" />
    </configSections>

Second, set on `ResultsetRuntimeVerification` switch
    <FSharp.Data.SqlClient>
        <add key="ResultsetRuntimeVerification" value="true"/>
    </FSharp.Data.SqlClient>

Other debugging/instrumentation tools to consider:
-------------------------------------

[Sql Profiler](https://msdn.microsoft.com/en-us/library/ms181091.aspx)
[ADO.NET tracing](https://msdn.microsoft.com/en-us/library/cc765421.aspx)
[Dynamic views](https://azure.microsoft.com/en-us/documentation/articles/sql-database-monitoring-with-dmvs/) 
[Query store](https://azure.microsoft.com/en-us/blog/query-store-a-flight-data-recorder-for-your-database/)

*)
