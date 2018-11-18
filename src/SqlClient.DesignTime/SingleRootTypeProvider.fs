namespace ProviderImplementation.ProvidedTypes

open FSharp.Data.SqlClient
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System

[<AbstractClass>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type SingleRootTypeProvider(config: TypeProviderConfig, providerName, parameters, ?isErased) as this = 
    inherit TypeProviderForNamespaces (config, assemblyReplacementMap=[("FSharp.Data.SqlClient.DesignTime", "FSharp.Data.SqlClient")], addDefaultProbingLocation=true)

    let cache = new Cache<ProvidedTypeDefinition>()
    do 
        let isErased = defaultArg isErased true
        let nameSpace = this.GetType().Namespace
        let assembly = Assembly.GetExecutingAssembly()

        let providerType = ProvidedTypeDefinition(assembly, nameSpace, providerName, Some typeof<obj>, hideObjectMethods = true, isErased = isErased)

        providerType.DefineStaticParameters(
            parameters = parameters,             
            instantiationFunction = fun typeName args ->
                let typ, monitors = this.CreateRootType(assembly, nameSpace, typeName, args)
                cache.GetOrAdd(typeName, typ)
        )

        this.AddNamespace( nameSpace, [ providerType ])

    abstract CreateRootType: assemblyName: Assembly * nameSpace: string * typeName: string  * args: obj[] -> Lazy<ProvidedTypeDefinition> * obj[] // ChangeMonitor[] underneath but there is a problem https://github.com/fsprojects/FSharp.Data.SqlClient/issues/234#issuecomment-240694390
