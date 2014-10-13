(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"

(**
FSharp.Data.SqlEnumProvider
===========================

Motivation
-------------------------------------
Often there is a certain amount of reference/lookup data in a database. 
This information changes relatively rare. 
At same time it's never represented in an application types. 

Here is the specific example. We'll use AdventureWorks2012 as sample database.

Let's say we need to retrieve number of orders shipped in certain way since specific date.

Order shippment types defined in Purchasing.ShipMethod table. 
    
    [lang=sql]
    SELECT Name, ShipMethodID FROM Purchasing.ShipMethod

The query returns:

<table >
<thead><tr><td>Name</td><td>ShipMethodID</td></tr></thead>
<tbody>
    <tr><td>CARGO TRANSPORT 5</td><td>5</td></tr>
    <tr><td>OVERNIGHT J-FAST</td><td>4</td></tr>
    <tr><td>OVERSEAS - DELUXE</td><td>3</td></tr>
    <tr><td>XRQ - TRUCK GROUND</td><td>1</td></tr>
    <tr><td>ZY - EXPRESS</td><td>2</td></tr>
</tbody>
</table>

A typical implementation for overnight orders shipped since Jan 1, 2008 is following:
*)

[<Literal>]
let connStr = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

open System 
open System.Data.SqlClient

let conn = new SqlConnection (connStr)
conn.Open()

let cmd = new SqlCommand ("
    SELECT COUNT(*) 
    FROM Purchasing.PurchaseOrderHeader 
    WHERE ShipDate > @shippedLaterThan AND ShipMethodID = @shipMethodId", conn)

cmd.Parameters.AddWithValue("@shippedLaterThan", DateTime(2008, 1, 1)) |> ignore
cmd.Parameters.AddWithValue("@shipMethodId", 4) |> ignore
cmd.ExecuteScalar() |> unbox<int>

(**
The query returns valid answer 748 but suffers from a serious issue - it uses  magic number (4).
  
The problem can alleviated by ad-hoc enum definition:
*)

type ShippingMethod = 
    | ``XRQ - TRUCK GROUND`` = 1
    | ``ZY - EXPRESS`` = 2
    | ``OVERSEAS - DELUXE`` = 3
    | ``OVERNIGHT J-FAST`` = 4
    | ``CARGO TRANSPORT 5`` = 5
    
cmd.Parameters.AddWithValue("@shippedLaterThan", DateTime(2008, 1, 1)) |> ignore
cmd.Parameters.AddWithValue("@shipMethodId", ShippingMethod.``OVERNIGHT J-FAST``) |> ignore
cmd.ExecuteScalar() |> unbox<int>

(**
But improvement is questionable because we traded one problem for another – keeping this enum type definition in sync with database changes.  

## Solution - SqlEnumProvider

### F# idiomatic enum-like type

Idea is to generate enumeration like type based on a query to database leveraging F# type providers feature. 

The code above can be rewritten as follows:

*)

open FSharp.Data

//by convention: first column is Name, second is Value
type ShipMethod = SqlEnumProvider<"SELECT Name, ShipMethodID FROM Purchasing.ShipMethod ORDER BY ShipMethodID", connStr>

//Now combining 2 F# type providers: SqlEnumProvider and SqlCommandProvider
type OrdersByShipTypeSince = SqlCommandProvider<"
    SELECT COUNT(*) 
    FROM Purchasing.PurchaseOrderHeader 
    WHERE ShipDate > @shippedLaterThan AND ShipMethodID = @shipMethodId", connStr, SingleRow = true>

let cmd2 = new OrdersByShipTypeSince() 
cmd2.Execute( DateTime( 2008, 1, 1), ShipMethod.``OVERNIGHT J-FAST``) |> Option.get |> Option.get  

(**
This type has semantics similar to standard BCL Enum type and stays in sync with database SqlEnumProvider pointed to. 
We get readability, up-to date lookup data verified by compiler and intellisense. 

**Important presumption that source reference data is synchronized with production environment** because SqlEnumProvider usually points to development environment database.

The SQL statement used to query database has to return resultset of certain shape:

    - It has to have 2 or more columns
    - The first columns is unique name (or tag/label).
    - The second column is value. In contrast to BCL Enum it can not only numeric type but also 
    decimal, DateTime, DateTimeOffet or string
    - If value consists of more than one column it represented as Tuple. It makes it similar to Java enums 

Again the idea is straightforward – anywhere in the code tag/label will be replace by value: ``XRQ - TRUCK GROUND`` with 1, ``ZY - EXPRESS`` with 1, etc.

The `ShipMethod` type provides more idiomatic interface that standard BCL Enum:
  - `Items` is read-only field of `list<string * 'T>` where `'T` is type of value
  - `TryParse` return `option<'T>`

Below are sample invocations and output from FSI:

*)

//Utility methods - provide more idiomatic F# interface
ShipMethod.Items
//val it : List<string * int> =
//  [("XRQ - TRUCK GROUND", 1); ("ZY - EXPRESS", 2); ("OVERSEAS - DELUXE", 3);
//   ("OVERNIGHT J-FAST", 4); ("CARGO TRANSPORT 5", 5)]

ShipMethod.``CARGO TRANSPORT 5``
//val it : int = 5
ShipMethod.``OVERNIGHT J-FAST``
//val it : int = 4
ShipMethod.TryParse("CARGO TRANSPORT 5") 
//val it : Option<int> = Some 5
ShipMethod.TryParse("cargo transport 5") 
//val it : Option<int> = None
ShipMethod.TryParse("cargo transport 5", ignoreCase = true) 
//val it : Option<int> = Some 5
ShipMethod.TryParse("Unknown") 
//val it : Option<int> = None
ShipMethod.Parse("CARGO TRANSPORT 5") 
//val it : int = 5


(**  
F# tuple-valued enums
-------------------------------------
As mentioned above result set with more than 2 columns is mapped to enum with tuple as value. This makes it similar to [Java enums] (http://javarevisited.blogspot.com/2011/08/enum-in-java-example-tutorial.html).
*)

//ShipRate is included into the resultset in addition to ShipMethodID``` 
type ShipInfo = 
    SqlEnumProvider<"SELECT Name, ShipMethodID, ShipRate FROM Purchasing.ShipMethod ORDER BY ShipMethodID", connStr>

type TheLatestOrder = SqlCommandProvider<"
    SELECT TOP 1 * 
    FROM Purchasing.PurchaseOrderHeader 
    ORDER BY ShipDate DESC
    ", connStr, SingleRow = true>

let cmd3 = new TheLatestOrder() 
let theLatestOrder = cmd3.Execute().Value

//exploring multi-item value for application logic

//using the first item for conditional logic
if theLatestOrder.ShipMethodID = fst ShipInfo.``OVERSEAS - DELUXE``
then 
    //using the second item for computation
    printfn "Some calculation: %M" <| 50M * snd ShipInfo.``OVERSEAS - DELUXE``

(**
I have mixed feelings about applicability of multi-item value type. Please provide feedback/examples that prove it useful or otherwise._

CLI native enums
-------------------------------------

SqlEnumProvider is unique because it supports two type generation strategies: F# idiomatic enum-behaving type and standard CLI enumerated types. Second can be useful where compiler allows only const declaration - attribute constructors for example. Set "CLIEnum" parameter to generate standard enum.

*)

//CLI Enum

#r "System.Web.Http.dll" 
#r "System.Net.Http.dll" 
open System.Web.Http

type Roles = 
    SqlEnumProvider<"SELECT * FROM (VALUES(('Read'), 1), ('Write', 2), ('Admin', 4)) AS T(Name, Value)", @"Data Source=(LocalDb)\v11.0;Integrated Security=True", CLIEnum = true>

type CustomAuthorizeAttribute(roles: Roles) = 
    inherit AuthorizeAttribute()

    override __.OnAuthorization actionContext = 
        //auth logic here
        ()

[<CustomAuthorizeAttribute(Roles.Admin)>]
type MyController() = 
    inherit ApiController()

    member __.Get() = 
        Seq.empty<string>

(**
It also makes this types **accessible from C#** or any other .NET language.

### Multi-platform. 

##### Any ADO.NET supported database
SqlEnumProvider has a static parameter "Provider" which allows to pass ADO.NET provider [invariant name](http://msdn.microsoft.com/en-us/library/h508h681.aspx). This makes it usable with any ADO.NET supported database. “System.Data.SqlClient”  is default value for ADO.NET provider.

Invariant names of available ADO.NET providers can be retrieved as follows:
*)

open System.Data.Common
[ for r in DbProviderFactories.GetFactoryClasses().Rows -> r.["InvariantName"] ]

(**
##### Generated types accesable from C#, Visual Basic and other .NET languages
Show your fellow C#/VB developers magic of F# type provider accessible from their favorite language!!! Sample project is here. [Demo solution](https://github.com/dmitry-a-morozov/FSharp.Data.SqlEnumProvider/blob/master/demos/CSharp.Client/Program.cs) includes example.

##### Xamarin ???
The type provider should work in XS when a project targets Mono runtime. Nothing technically stops to make it available for Xamarin supported mobile platforms (iOS, Android & Windows Phone) to access SQLite.

##### Future extensions: [FlagsAttribute](http://msdn.microsoft.com/en-us/library/system.flagsattribute.aspx) Enums ?

###Educational
F# developers often ask about simple examples of “generated types” type providers. Here you go. I hope it will be useful.
*)
