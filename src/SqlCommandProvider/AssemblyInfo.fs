namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("SqlCommandProvider")>]
[<assembly: AssemblyProductAttribute("FSharp.Data.SqlClient")>]
[<assembly: AssemblyDescriptionAttribute("SqlClient F# type providers")>]
[<assembly: AssemblyVersionAttribute("1.2.1")>]
[<assembly: AssemblyFileVersionAttribute("1.2.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.2.1"
