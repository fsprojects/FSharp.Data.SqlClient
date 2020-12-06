namespace FSharp.Data.SqlClient.Internals

//this is mess. Clean up later.
type FsharpDataSqlClientConfiguration = {
    ResultsetRuntimeVerification: bool
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<AutoOpen>]
module Configuration = 
    let internal guard = obj()
    let mutable internal current = { FsharpDataSqlClientConfiguration.ResultsetRuntimeVerification = false }

    type FsharpDataSqlClientConfiguration with
        static member Current 
            with get() = lock guard <| fun() -> current
            and set value = lock guard <| fun() -> current <- value

#if WITH_LEGACY_NAMESPACE
namespace FSharp.Data.SqlClient
open System
[<Obsolete("use 'FSharp.Data.SqlClient.Internals.FsharpDataSqlClientConfiguration' instead");AutoOpen>]
type Configuration = FSharp.Data.SqlClient.Internals.FsharpDataSqlClientConfiguration
namespace FSharp.Data
open System
open FSharp.Data.SqlClient.Internals
open FSharp.Data.SqlClient.Internals.Configuration
[<Obsolete("use open 'FSharp.Data.SqlClient.Internals' namespace instead")>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<AutoOpen>]
module Configuration = 
    type FsharpDataSqlClientConfiguration with
       static member Current 
           with get() = lock Configuration.guard <| fun() -> Configuration.current
           and set value = lock Configuration.guard <| fun() -> Configuration.current <- value

#endif