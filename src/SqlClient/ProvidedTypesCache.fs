[<AutoOpen>]
module FSharp.Data.SqlClient.Cache

open System
open System.Runtime.Caching

type MemoryCache with 
    member this.GetOrAdd<'T>(key, value: Lazy<'T>, ?expiration): 'T = 
        let policy = CacheItemPolicy()
        policy.SlidingExpiration <- defaultArg expiration <| TimeSpan.FromHours 24.
        match this.AddOrGetExisting(key, value, policy) with
        | :? Lazy<'T> as item -> 
            item.Value
        | x -> 
            assert(x = null)
            value.Value

