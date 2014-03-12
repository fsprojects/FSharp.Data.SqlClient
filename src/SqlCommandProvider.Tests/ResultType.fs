module FSharp.Data.Tests.ResultType 

open FSharp.Data
open Xunit
open FsUnit.Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type ResultTypeReader = 
    SqlCommandProvider<"SELECT * FROM (VALUES ('F#', 2005), ('Scala', 2003), ('foo bar',NULL))  AS T(lang, DOB)", "name=AdventureWorks2012", ResultType = ResultType.DataReader>

let ReadToMaps(reader : System.Data.SqlClient.SqlDataReader) = seq{
            try 
                while(reader.Read()) do
                    yield   Map.ofArray<string, obj> [| 
                                for i = 0 to reader.FieldCount - 1 do
                                        if not(reader.IsDBNull(i)) then yield reader.GetName(i), reader.GetValue(i)
                            |]  

            finally
                reader.Close()
           }
[<Fact>]
let ResultTypeReader() = 
    let cmd = ResultTypeReader()
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
