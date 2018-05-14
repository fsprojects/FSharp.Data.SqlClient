[<AutoOpen>]
module internal FSharp.Data.SqlClient.Cache

open ProviderImplementation.ProvidedTypes
open System.Collections.Concurrent

type TypeName = string

type Cache<'a>() =
    let cache = ConcurrentDictionary<TypeName, Lazy<'a>>()

    member __.GetOrAdd(typeName: TypeName, value: Lazy<'a>): 'a = 
        cache.GetOrAdd(typeName, value).Value

