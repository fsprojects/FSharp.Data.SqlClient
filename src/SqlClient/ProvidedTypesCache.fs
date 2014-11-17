namespace ProviderImplementation.ProvidedTypes

open System
open System.Runtime.Caching

type ProvidedTypesCache(tp: TypeProviderForNamespaces) = 
    
    static let defaultExpiration = TimeSpan.FromSeconds 10.
    
    let cache = new MemoryCache(tp.GetType().Name)
    
    do 
        tp.Disposing.Add(fun _ -> cache.Dispose())

    member __.GetOrAdd(key: obj, item: Lazy<ProvidedTypeDefinition>) = 
        match cache.AddOrGetExisting(string key, item, CacheItemPolicy(SlidingExpiration = defaultExpiration)) with
        | :? Lazy<ProvidedTypeDefinition> as item -> item.Value
        | x -> 
            assert(x = null)
            item.Value

    member __.Remove(key: obj) = cache.Remove(string key) |> ignore

    interface IDisposable with member __.Dispose() = cache.Dispose()