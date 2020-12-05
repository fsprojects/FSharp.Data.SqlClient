open FSharp.Data.SqlClient

[<Literal>]
let connectionString = "Data Source=localhost;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<EntryPoint>]
let main _ =
    let get42 = new SqlCommandProvider<"SELECT 42", connectionString>(connectionString)
    get42.Execute() |> Seq.toArray |> printfn "%A"
    0
