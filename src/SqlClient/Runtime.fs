namespace FSharp.Data.SqlClient

#if !IS_DESIGNTIME
// Put the TypeProviderAssemblyAttribute in the runtime DLL, pointing to the design-time DLL
[<assembly:CompilerServices.TypeProviderAssembly("FSharp.Data.SqlClient.DesignTime.dll")>]
do ()
#endif
