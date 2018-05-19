namespace System
open System.Reflection
open System.Runtime.CompilerServices

[<assembly: AssemblyTitleAttribute("SqlClient")>]
[<assembly: AssemblyProductAttribute("FSharp.Data.SqlClient")>]
[<assembly: AssemblyDescriptionAttribute("SqlClient F# type providers")>]
[<assembly: AssemblyVersionAttribute("1.8.4")>]
[<assembly: AssemblyFileVersionAttribute("1.8.4")>]
[<assembly: InternalsVisibleToAttribute("SqlClient.Tests")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.8.4"
