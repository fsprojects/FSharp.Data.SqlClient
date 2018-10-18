
open FSharp.Data

let get42 = new SqlCommandProvider<"SELECT 42", "Server=.;Integrated Security=True">("Server=.;Integrated Security=True")
get42.Execute() |> Seq.toArray |> printfn "%A"

