* 1.0.015 - Initial release as part of fsprojects
* 1.0.016 - Issue #32 fixed
* 1.0.017 - Fixing FallbackToProbeResultTypeInTransaction value is not respected
* 1.0.018 - Parametrized queries that can fallback to SET FMTONLY ON
* 1.0.019 - Removing FallbackToProbeResultTypeInTransaction
* 1.0.020 - Support ResultType.Maps for untyped output data
* 1.1.21 - Preserve original exception thrown by sys.spdescribefirstresult_set.
* 1.1.22 - Change TVP representation from tuples to custom types with ctor
* 1.1.23 - Fix issue #35. Change lazy ctor generation to eager.
* 1.1.24 - Fix issue #34. Inferred reference types assumed to be non-nullable.
* 1.1.25 - Fix issue #38. Support for serialization in WebAPI. ResultType.Records erased to ExpandoObject.
* 1.1.27 - Fix issues #39 and #40. Mandatory ConnectionStringOrName parameter. ConnectionStringOrName static property.
* 1.1.28 - BREKAING CHANGE: default ResultType is now Records. Additional xml docs. Fix Issue #43. SingleRow = true now returns option<_> instead of just value. Bugfix for FileWatcher on VS 2013. Transaction support in SqlCommand constructor. Design-time performace improvements with caching.
* 1.1.29 - Fix issue #46 : support for SqlTypes - note that additional Nuget package is now required. Minor improvement in design-time opening of the command file.
* 1.1.30 - Adding SqlTypes dll for design-time support
* 1.1.31 - Connection management issue. Upgdate ASAP.
* 1.2.0 - Dropping Experimental from the name, adding Programmability to nuget.
* 1.2.1 - Separating namespaces for the providers.
* 1.2.2 - Merging assemblies.
* 1.2.3 - Fixing nuget package.
* 1.2.4 - CommandType removed from SqlCommandProvider.
* 1.2.5 - Timeout on reading command file.
* 1.2.6 - Bugfix for nullable string iput parameters.
* 1.2.7 - Runtime SqlCommand<> introduced in SqlCommandProvider. "True" synchronous Execute implemented.
* 1.2.8 - Downgrading back to 4.0 and FSharp.Core 4.3.0
* 1.2.9 - Making input string into string option when allParametersOptional is suplied in SqlCommandProvider
* 1.2.10 - Introduce runtime base class for the Record. ConcurrentDictionary for cache.
* 1.2.11 - With method on RuntimeRecord added.
* 1.2.12 - IDictionary<string,obj> is implemented on RuntimeRecord.
* 1.2.13 - Fixing GetHashCode. Renaming RuntimeRecord.
* 1.2.14 - Fix for bit default value. Changes in RuntimeRecord.
* 1.2.15 - bug fix for string param with AllParametersOptional.
* 1.2.16 - Fixing Azure compatibility for SqlProgrammability.
* 1.2.17 - Mono compatibility is back.
* 1.2.18 - More Mono compatibility. Set TypeName property on SqlParameter only for TVP.