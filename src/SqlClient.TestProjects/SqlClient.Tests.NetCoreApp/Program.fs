open FSharp.Data.SqlClient
open FSharp.Data

[<Literal>]
let Cnx = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"
(*
type SingleColumnSelect = SqlEnumProvider<"SELECT Name FROM Purchasing.ShipMethod", Cnx>
type TinyIntEnum = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", Cnx, Kind = SqlEnumKind.CLI>
type CurrencyCodeUOM = 
   SqlEnumProvider<"
       SELECT CurrencyCode
       FROM Sales.Currency 
       WHERE CurrencyCode IN ('USD', 'EUR', 'GBP')
   ", Cnx, Kind = SqlEnumKind.UnitsOfMeasure>
*)
[<EntryPoint>]
let main _ =
    let get42 = new SqlCommandProvider<"SELECT 42", Cnx>(Cnx)
    get42.Execute() |> Seq.toArray |> printfn "SqlCommandTest: %A"


    (*
    printfn "SqlEnum default test: %A" SingleColumnSelect.``CARGO TRANSPORT 5``
    printfn "SqlEnum CLI enum test: %A" TinyIntEnum.One
    printfn "SqlEnum UOM test: %A" 1m<CurrencyCodeUOM.USD>
    *)

    0