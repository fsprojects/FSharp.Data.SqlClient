namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("SqlClient")>]
[<assembly: AssemblyProductAttribute("FSharp.Data.SqlClient")>]
[<assembly: AssemblyDescriptionAttribute("SqlClient F# type providers")>]
[<assembly: AssemblyVersionAttribute("1.3.4")>]
[<assembly: AssemblyFileVersionAttribute("1.3.4")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.3.4"
