namespace ProviderImplementation.ProvidedTypes

open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System.Runtime.Caching
open System
open System.Collections.Concurrent

type CacheWithMonitors (providerName) =
    //cache
    let changeMonitors = ConcurrentBag()
    [<VolatileField>]
    let mutable isDisposing = false
    let cache = new MemoryCache(providerName)

    member x.ClearCache() = 
        while not changeMonitors.IsEmpty do
            let removed, (monitor: CacheEntryChangeMonitor) =  changeMonitors.TryTake()
            if removed then monitor.Dispose()
        
        // http://stackoverflow.com/a/29916907
        cache 
        |> Seq.map (fun e -> e.Key) 
        |> Seq.toArray
        |> Array.iter (cache.Remove >> ignore)

    member x.CacheGetOrAdd(key, value: Lazy<ProvidedTypeDefinition>, monitors, invalidate) = 
        let policy = CacheItemPolicy()
        monitors |> Seq.iter policy.ChangeMonitors.Add
        let existing = cache.AddOrGetExisting(key, value, policy)
        let cacheItem = 
            if existing = null 
            then 
                let m = cache.CreateCacheEntryChangeMonitor [ key ]
                m.NotifyOnChanged(fun _ -> 
                    x.ClearCache()
                    invalidate()
                )
                changeMonitors.Add(m)
                value 
            else 
                unbox existing

        cacheItem.Value

    interface IDisposable with
        member x.Dispose () =
            if not isDisposing then
                isDisposing <- true
                x.ClearCache()
                cache.Dispose()



[<AbstractClass>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type SingleRootTypeProvider(config: TypeProviderConfig, providerName, parameters, ?isErased) as this = 
    inherit TypeProviderForNamespaces(config)

    let cache = new CacheWithMonitors(providerName)
    do 
        let isErased = defaultArg isErased true
        let nameSpace = this.GetType().Namespace
        let assembly = Assembly.LoadFrom( config.RuntimeAssembly)

        let providerType = ProvidedTypeDefinition(assembly, nameSpace, providerName, Some typeof<obj>, hideObjectMethods = true, isErased = isErased)

        providerType.DefineStaticParameters(
            parameters = parameters,             
            instantiationFunction = fun typeName args ->
                let typ, monitors = this.CreateRootType(assembly, nameSpace, typeName, args)
                cache.CacheGetOrAdd(typeName, typ, monitors |> Seq.cast, this.Invalidate)
        )

        this.AddNamespace( nameSpace, [ providerType ])

    do
        this.Disposing.Add <| fun _ -> (cache :> IDisposable).Dispose()

    abstract CreateRootType: assemblyName: Assembly * nameSpace: string * typeName: string  * args: obj[] -> Lazy<ProvidedTypeDefinition> * obj[] // ChangeMonitor[] underneath but there is a problem https://github.com/fsprojects/FSharp.Data.SqlClient/issues/234#issuecomment-240694390
