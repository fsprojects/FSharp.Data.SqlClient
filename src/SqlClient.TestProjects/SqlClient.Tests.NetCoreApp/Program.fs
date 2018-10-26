open FSharp.Data

[<Literal>]
let cnx = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type SingleColumnSelect = SqlEnumProvider<"SELECT Name FROM Purchasing.ShipMethod", cnx>
type TinyIntEnum = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", cnx, Kind = SqlEnumKind.CLI>
type CurrencyCodeUOM = 
   SqlEnumProvider<"
       SELECT CurrencyCode
       FROM Sales.Currency 
       WHERE CurrencyCode IN ('USD', 'EUR', 'GBP')
   ", cnx, Kind = SqlEnumKind.UnitsOfMeasure>

[<EntryPoint>]
let main _ =
    let get42 = new SqlCommandProvider<"SELECT 42", "Server=.;Integrated Security=True">("Server=.;Integrated Security=True")
    get42.Execute() |> Seq.toArray |> printfn "SqlCommandTest: %A"

    printfn "SqlEnum default test: %A" SingleColumnSelect.``CARGO TRANSPORT 5``    
    printfn "SqlEnum CLI enum test: %A" TinyIntEnum.One
    printfn "SqlEnum UOM test: %A" 1m<CurrencyCodeUOM.USD>    
    0