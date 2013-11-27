namespace System
open System.Reflection
open Microsoft.FSharp.Core.CompilerServices

[<assembly: AssemblyTitleAttribute("SqlCommandTypeProvider")>]
[<assembly: AssemblyProductAttribute("SqlCommandTypeProvider")>]
[<assembly: AssemblyDescriptionAttribute("SqlCommand F# type provider")>]
[<assembly: AssemblyVersionAttribute("1.0.14")>]
[<assembly: AssemblyFileVersionAttribute("1.0.14")>]
[<assembly:TypeProviderAssembly()>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.14"
