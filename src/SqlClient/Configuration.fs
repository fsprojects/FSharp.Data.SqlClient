namespace FSharp.Data.SqlClient

//this is mess. Clean up later.
type Configuration = {
    ResultsetRuntimeVerification: bool
}   

namespace FSharp.Data

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<AutoOpen>]
module Configuration = 
    let private guard = obj()
    let mutable private current = { SqlClient.Configuration.ResultsetRuntimeVerification = false }

    type SqlClient.Configuration with
        static member Current 
            with get() = lock guard <| fun() -> current
            and set value = lock guard <| fun() -> current <- value
