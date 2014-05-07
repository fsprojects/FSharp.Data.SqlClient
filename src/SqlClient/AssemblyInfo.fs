namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("SqlClient")>]
[<assembly: AssemblyProductAttribute("FSharp.Data.SqlClient")>]
[<assembly: AssemblyDescriptionAttribute("SqlClient F# type providers")>]
[<assembly: AssemblyVersionAttribute("1.2.21")>]
[<assembly: AssemblyFileVersionAttribute("1.2.21")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.2.21"
