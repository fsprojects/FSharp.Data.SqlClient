namespace ProviderImplementation.ProvidedTypes

open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System.Runtime.Caching
open System
open System.Collections.Concurrent

[<AbstractClass>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type SingleRootTypeProvider(config: TypeProviderConfig, providerName, parameters, ?isErased) as this = 
    inherit TypeProviderForNamespaces()

    //cache
    let changeMonitors = ConcurrentBag()
    [<VolatileField>]
    let mutable clearingCache = false
    let cache = new MemoryCache(providerName)

    let clearCache() = 
        clearingCache <- true
        while not changeMonitors.IsEmpty do
            let removed, (monitor: CacheEntryChangeMonitor) =  changeMonitors.TryTake()
            if removed then monitor.Dispose()
        
        // http://stackoverflow.com/a/29916907
        cache 
        |> Seq.map (fun e -> e.Key) 
        |> Seq.toArray
        |> Array.iter (cache.Remove >> ignore)

    let cacheGetOrAdd(key, value: Lazy<ProvidedTypeDefinition>, monitors) = 
        let policy = CacheItemPolicy()
        monitors |> Seq.iter policy.ChangeMonitors.Add
        let existing = cache.AddOrGetExisting(key, value, policy)
        let cacheItem = 
            if existing = null 
            then 
                let m = cache.CreateCacheEntryChangeMonitor [ key ]
                m.NotifyOnChanged(fun _ -> 
                    if not clearingCache 
                    then 
                        clearCache()
                        this.Invalidate()
                )
                changeMonitors.Add(m)
                value 
            else 
                unbox existing

        cacheItem.Value

    do 
        let isErased = defaultArg isErased true
        let nameSpace = this.GetType().Namespace
        let assembly = Assembly.LoadFrom( config.RuntimeAssembly)

        let providerType = ProvidedTypeDefinition(assembly, nameSpace, providerName, Some typeof<obj>, HideObjectMethods = true, IsErased = isErased)

        providerType.DefineStaticParameters(
            parameters = parameters,             
            instantiationFunction = fun typeName args ->
                let typ, monitors = this.CreateRootType(assembly, nameSpace, typeName, args)
                cacheGetOrAdd(typeName, typ, monitors |> Seq.cast)
        )

        this.AddNamespace( nameSpace, [ providerType ])

    do
        this.Disposing.Add <| fun _ -> clearCache()

    abstract CreateRootType: assemblyName: Assembly * nameSpace: string * typeName: string  * args: obj[] -> Lazy<ProvidedTypeDefinition> * obj[] // ChangeMonitor[] underneath but there is a problem https://github.com/fsprojects/FSharp.Data.SqlClient/issues/234#issuecomment-240694390
