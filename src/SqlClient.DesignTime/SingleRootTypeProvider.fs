namespace ProviderImplementation.ProvidedTypes

open FSharp.Data.SqlClient
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System.Runtime.InteropServices
open System

[<AbstractClass>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.",
                           101,
                           IsHidden = true)>]
type SingleRootTypeProvider(config: TypeProviderConfig, providerName, parameters, ?isErased) as this =
    inherit
        TypeProviderForNamespaces(
            config,
            assemblyReplacementMap = [ ("FSharp.Data.SqlClient.DesignTime", "FSharp.Data.SqlClient") ],
            addDefaultProbingLocation = true
        )

    // On Windows, Microsoft.Data.SqlClient loads the native SNI library via P/Invoke.
    // When the type provider runs inside the F# compiler, the native DLL search path
    // may not include the TP output directory. Register a resolver so the runtime can
    // find Microsoft.Data.SqlClient.SNI.dll next to the TP assembly.
#if USE_SYSTEM_DATA_COMMON_DBPROVIDERFACTORIES
    static do
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            let tpDir =
                System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

            let handler =
                System.Runtime.InteropServices.DllImportResolver(fun libraryName assembly searchPath ->
                    if libraryName = "Microsoft.Data.SqlClient.SNI.dll" then
                        let candidate = System.IO.Path.Combine(tpDir, "Microsoft.Data.SqlClient.SNI.dll")

                        if System.IO.File.Exists(candidate) then
                            NativeLibrary.Load(candidate)
                        else
                            System.IntPtr.Zero
                    else
                        System.IntPtr.Zero)

            NativeLibrary.SetDllImportResolver(typeof<Microsoft.Data.SqlClient.SqlConnection>.Assembly, handler)
#endif

    let cache = new Cache<ProvidedTypeDefinition>()

    do
        let isErased = defaultArg isErased true
        let nameSpace = this.GetType().Namespace
        let assembly = Assembly.GetExecutingAssembly()

        let providerType =
            ProvidedTypeDefinition(
                assembly,
                nameSpace,
                providerName,
                Some typeof<obj>,
                hideObjectMethods = true,
                isErased = isErased
            )

        providerType.DefineStaticParameters(
            parameters = parameters,
            instantiationFunction =
                fun typeName args ->
                    match cache.TryGetValue(typeName) with
                    | true, cachedType -> cachedType.Value
                    | false, _ ->
                        let typ, monitors = this.CreateRootType(assembly, nameSpace, typeName, args)

                        monitors
                        |> Seq.iter (fun m ->
                            match m with
                            | :? System.Runtime.Caching.ChangeMonitor as monitor ->
                                monitor.NotifyOnChanged(fun _ ->
                                    cache.Remove(typeName)
                                    this.Invalidate()
                                    monitor.Dispose())
                            | _ -> ())

                        cache.GetOrAdd(typeName, typ)
        )

        this.AddNamespace(nameSpace, [ providerType ])

    abstract CreateRootType:
        assemblyName: Assembly * nameSpace: string * typeName: string * args: obj[] ->
            Lazy<ProvidedTypeDefinition> * obj[] // ChangeMonitor[] underneath but there is a problem https://github.com/fsprojects/FSharp.Data.SqlClient/issues/234#issuecomment-240694390
