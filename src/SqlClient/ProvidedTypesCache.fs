[<AutoOpen>]
module ProviderImplementation.ProvidedTypes.MemoryCache

open System
open System.Runtime.Caching

type MemoryCache with 
    member this.GetOrAdd(key, value: Lazy<_>, ?expiration) = 
        let policy = CacheItemPolicy()
        policy.SlidingExpiration <- defaultArg expiration <| TimeSpan.FromHours 24.
        match this.AddOrGetExisting(key, value, policy) with
        | :? Lazy<ProvidedTypeDefinition> as item -> 
            item.Value
        | x -> 
            assert(x = null)
            value.Value
