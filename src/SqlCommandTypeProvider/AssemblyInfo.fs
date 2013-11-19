namespace System
open System.Reflection
open Microsoft.FSharp.Core.CompilerServices

[<assembly: AssemblyTitleAttribute("SqlCommandTypeProvider")>]
[<assembly: AssemblyProductAttribute("SqlCommandTypeProvider")>]
[<assembly: AssemblyDescriptionAttribute("SqlCommand F# type provider")>]
[<assembly: AssemblyVersionAttribute("1.0.12")>]
[<assembly: AssemblyFileVersionAttribute("1.0.12")>]
[<assembly:TypeProviderAssembly()>]
()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.12"
