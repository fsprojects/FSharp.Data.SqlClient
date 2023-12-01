module FSharp.Data.SqlClient.Tests.ResultTypeTests
open FSharp.Data
open FSharp.Data.SqlClient
open FSharp.Data.SqlClient.Tests

open FSharp.Data
open Xunit

[<Literal>]
let connectionString = ConnectionStrings.AdventureWorksNamed

[<Literal>]
let command = "SELECT * FROM (VALUES ('F#', 2005), ('Scala', 2003), ('foo bar',NULL))  AS T(lang, DOB)"

type ResultTypeReader = SqlCommandProvider<command, ConnectionStrings.AdventureWorksNamed, ResultType = ResultType.DataReader>

let ReadToMaps(reader : System.Data.SqlClient.SqlDataReader) = 
    seq {
        try 
            while(reader.Read()) do
                yield [| 
                    for i = 0 to reader.FieldCount - 1 do
                        if not(reader.IsDBNull(i)) then yield reader.GetName(i), reader.GetValue(i)
                |] |> Map.ofArray
        finally
            reader.Close()
    }

[<Fact>]
let ResultTypeReader() = 
    use cmd = new ResultTypeReader()
    let expected = 
        [| 
            "F#", Some 2005 
            "Scala", Some 2003
            "foo bar", None
        |] 
        |> Array.map (fun(name, value) ->
            let result = Map.ofList [ "lang", box name ]
            match value with
            | Some x -> result.Add("DOB", box x)
            | None -> result
        )

    Assert.Equal<Map<string, obj>[]>(expected, ReadToMaps(cmd.Execute()) |> Seq.toArray)
