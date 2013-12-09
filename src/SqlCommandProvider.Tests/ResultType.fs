module FSharp.Data.Experimental.Tests.ResultType 

open FSharp.Data.Experimental
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type ResultTypeMaps = 
    SqlCommand<"SELECT * FROM (VALUES ('F#', 2005), ('Scala', 2003)) AS T(lang, DOB)", ConnectionStringName = "AdventureWorks2012", ResultType = ResultType.Maps>

[<Fact>]
let ResultTypeMaps() = 
    let cmd = ResultTypeMaps()
    let expected = 
        [| 
            "F#", 2005
            "Scala", 2003 
        |] 
        |> Array.map (fun(lang, dob) ->
            Map.ofList [ "lang", box lang; "DOB", box dob]
        )

    Assert.Equal<Map<string, obj>[]>(expected, cmd.Execute() |> Seq.toArray)

type ResultTypeMapsWithNullableCols = 
    SqlCommand<"SELECT * FROM (VALUES ('abc', 123), ('def', 456), ('xyz', NULL)) AS T(name, value)", ConnectionStringName = "AdventureWorks2012", ResultType = ResultType.Maps>

[<Fact>]
let ResultTypeMapsWithNullableCols() = 
    let cmd = ResultTypeMapsWithNullableCols()
    let expected = 
        [| 
            "abc", Some 123 
            "def", Some 456
            "xyz", None
        |] 
        |> Array.map (fun(name, value) ->
            let result = Map.ofList [ "name", box name ]
            match value with
            | Some x -> result.Add("value", box x)
            | None -> result
        )

    Assert.Equal<Map<string, obj>[]>(expected, cmd.Execute() |> Seq.toArray)

