Support for .NET Core
=====================

As of version 2.0.1, a `netstandard2.0`-targeted version of FSharp.Data.SqlClient is available. 
This means you should be able to reference and use the type provider from a .NET Core application, 
with the following caveats and exceptions:

* The type provider is split into two different assemblies, `FSharp.Data.SqlClient.dll` 
(the runtime component or RTC) and `FSharp.Data.SqlClient.DesignTime.dll` (the design-time component or DTC).
  * The RTC (and its dependencies) must be available at runtime; they end up in the bin folder
  alongside your application's compiled assemblies. The FSharp.Data.SqlClient RTC is available for
  both `net40` and `netstandard2.0` and is thus fully compatible with .NET Core 2.0 applications.
  * The DTC is required to make the provided types available to your design-time tooling and to the
  compiler. The FSharp.Data.SqlClient DTC *has .NET Framework-only dependencies* (`Microsoft.SqlServer.Types`
  and `Microsoft.SqlServer.TransactSql.ScriptDom`). As such, either .NET Framework or Mono must be
  installed in order to compile your .NET Core application. Additionally, you must import `fsc.props`
  in your .fsproj/.csproj file - please see more detailed instructions [here](https://github.com/Microsoft/visualfsharp/issues/3303).
* As mentioned above, `Microsoft.SqlServer.Types` only targets .NET Framework, so you will not be able to use types such as [HierarchyId](http://technet.microsoft.com/en-us/library/bb677173.aspx) or
[spatial types](http://blogs.msdn.com/b/adonet/archive/2013/12/09/microsoft-sqlserver-types-nuget-package-spatial-on-azure.aspx) from your .NET Core app.
