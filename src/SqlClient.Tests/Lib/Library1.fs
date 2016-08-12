module DataAccess

open FSharp.Data

type Get42 = SqlCommandProvider<"SELECT 42", "name=AdventureWorks", SingleRow = true>

let get42() =
    use cmd = new Get42()
    cmd.Execute()