namespace FSharp.Data.SqlClient

#if !IS_DESIGNTIME
// Put the TypeProviderAssemblyAttribute in the runtime DLL, pointing to the design-time DLL

#if SYSTEM_DATA_SQLCLIENT
[<assembly:CompilerServices.TypeProviderAssembly("FSharp.Data.SqlClient.DesignTime.dll")>]
#endif
#if MICROSOFT_DATA_SQLCLIENT
[<assembly:CompilerServices.TypeProviderAssembly("FSharp.Data.MicrosoftSqlClient.DesignTime.dll")>]
#endif
do ()
#endif
