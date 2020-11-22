# FSharp.Data.SqlClient - Type providers for Microsoft SQL Server

This library exposes SQL Server Database objects in a type safe manner to F# code, by the mean of [Type Providers](https://docs.microsoft.com/en-us/dotnet/fsharp/tutorials/type-providers/)

## Quick Links
* [Documentation](http://fsprojects.github.io/FSharp.Data.SqlClient/)
* [Release Notes](RELEASE_NOTES.md)
* [Contribution Guide Lines](CONTRIBUTING.md)
* [Gitter Chat Room](https://gitter.im/fsprojects/FSharp.Data.SqlClient)

## Type Providers

### SqlCommandProvider 

Provides statically typed access to the parameters and result set of T-SQL command in idiomatic F# way <sup>(*)</sup>.

```fsharp
open FSharp.Data

[<Literal>]
let connectionString = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

// The query below retrieves top 3 sales representatives from North American region with YTD sales of more than one million.

do
    use cmd = new SqlCommandProvider<"
        SELECT TOP(@topN) FirstName, LastName, SalesYTD 
        FROM Sales.vSalesPerson
        WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
        ORDER BY SalesYTD
        " , connectionString>(connectionString)

    cmd.Execute(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) |> printfn "%A"
```
output
```fsharp
seq
    [("Pamela", "Ansman-Wolfe", 1352577.1325M);
     ("David", "Campbell", 1573012.9383M);
     ("Tete", "Mensa-Annan", 1576562.1966M)]
```

### SqlProgrammabilityProvider 

Exposes Tables, Stored Procedures, User-Defined Types and User-Defined Functions in F# code.

```fsharp
type AdventureWorks = SqlProgrammabilityProvider<connectionString>
do
    use cmd = new AdventureWorks.dbo.uspGetWhereUsedProductID(connectionString)
    for x in cmd.Execute( StartProductID = 1, CheckDate = System.DateTime(2013,1,1)) do
        //check for nulls
        match x.ProductAssemblyID, x.StandardCost, x.TotalQuantity with 
        | Some prodAsmId, Some cost, Some qty -> 
            printfn "ProductAssemblyID: %i, StandardCost: %M, TotalQuantity: %M" prodAsmId cost qty
        | _ -> ()
```
output
```fsharp
ProductAssemblyID: 749, StandardCost: 2171.2942, TotalQuantity: 1.00
ProductAssemblyID: 750, StandardCost: 2171.2942, TotalQuantity: 1.00
ProductAssemblyID: 751, StandardCost: 2171.2942, TotalQuantity: 1.00
```

### SqlEnumProvider

Let's say we need to retrieve number of orders shipped by a certain shipping method since specific date.

```fsharp
//by convention: first column is Name, second is Value
type ShipMethod = SqlEnumProvider<"
    SELECT Name, ShipMethodID FROM Purchasing.ShipMethod ORDER BY ShipMethodID", connectionString>

//Combine with SqlCommandProvider
do 
    use cmd = new SqlCommandProvider<"
        SELECT COUNT(*) 
        FROM Purchasing.PurchaseOrderHeader 
        WHERE ShipDate > @shippedLaterThan AND ShipMethodID = @shipMethodId
    ", connectionString, SingleRow = true>(connectionString) 
    //overnight orders shipped since Jan 1, 2008 
    cmd.Execute( System.DateTime( 2008, 1, 1), ShipMethod.``OVERNIGHT J-FAST``) |> printfn "%A"
```
output
```fsharp
Some (Some 1085)
```

### SqlFileProvider

```fsharp
type SampleCommand = SqlFile<"sampleCommand.sql">
type SampleCommandRelative = SqlFile<"sampleCommand.sql", "MySqlFolder">

use cmd1 = new SqlCommandProvider<SampleCommand.Text, ConnectionStrings.AdventureWorksNamed>()
use cmd2 = new SqlCommandProvider<SampleCommandRelative.Text, ConnectionStrings.AdventureWorksNamed>()
```

More information can be found in the [documentation](http://fsprojects.github.io/FSharp.Data.SqlClient/).

## Build Status

| Windows | Linux | NuGet |
|:-------:|:-----:|:-----:|
|[![Build status (Windows Server 2012, AppVeyor)](https://ci.appveyor.com/api/projects/status/gxou8oe4lt5adxbq)](https://ci.appveyor.com/project/fsgit/fsharp-data-sqlclient)|[![Build Status](https://travis-ci.org/fsprojects/FSharp.Data.SqlClient.svg?branch=master)](https://travis-ci.org/fsprojects/FSharp.Data.SqlClient)|[![NuGet Status](http://img.shields.io/nuget/v/FSharp.Data.SqlClient.svg?style=flat)](https://www.nuget.org/packages/FSharp.Data.SqlClient/)|

### Maintainers

- [@dmitry-a-morozov](https://github.com/dmitry-a-morozov)
- [@dsevastianov](https://github.com/dsevastianov)
- [@vasily-kirichenko](https://github.com/vasily-kirichenko) 
- [@smoothdeveloper](https://github.com/smoothdeveloper)

The default maintainer account for projects under "fsprojects" is [@fsprojectsgit](https://github.com/fsprojectsgit) - F# Community Project Incubation Space (repo management)

Thanks Jetbrains for their open source license program and providing their tool.
