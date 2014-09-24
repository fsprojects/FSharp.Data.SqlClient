module FSharp.Data.ResultTypeTests

open FSharp.Data
open Xunit
open FsUnit.Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>]
let command = "SELECT * FROM (VALUES ('F#', 2005), ('Scala', 2003), ('foo bar',NULL))  AS T(lang, DOB)"

type ResultTypeReader = SqlCommandProvider<command, "name=AdventureWorks2012", ResultType = ResultType.DataReader>

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


type ProductQuery = SqlCommandProvider<"SELECT * FROM Production.Product", "name=AdventureWorks2012", ResultType = ResultType.DataTable>

[<Fact>]
let DataTableHasKeyInfo() = 
    use cmd = new ProductQuery()
    let table = cmd.Execute()
    let productId = table.Columns.["ProductID"]
    Assert.True(productId.Unique)
    Assert.Equal<_ []>(table.PrimaryKey, [| productId |])
    

type ProductShortQuery = SqlCommandProvider<"SELECT Name, ProductNumber FROM Production.Product", "name=AdventureWorks2012", ResultType = ResultType.DataTable>

[<Fact>]
let DataTableRowCtor() = 
    use cmd = new ProductShortQuery()
    let table = cmd.Execute()
    let newRow = table.NewRow()
    ()

