module FSharp.Data.EnumTests

open System
open Xunit

type EnumMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", ConnectionStrings.LocalHost, CLIEnum = true>

[<Literal>]
let connectionString = ConnectionStrings.LocalHost

type TinyIntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", connectionString>

type MoreThan2Columns = SqlEnumProvider< @"
 select * from (
values 
  ('a', 1, 'this is a')
  , ('b', 2, 'this is b')
  , ('c', 3, 'this is c')
) as v(code, id, description)
", connectionString>

[<Fact>]
let tinyIntMapping() = 
    Assert.Equal<(string * byte) seq>([| "One", 1uy; "Two", 2uy |], TinyIntMapping.Items)

[<Fact>]
let parse() = 
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("one", ignoreCase = true))
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One", ignoreCase = false))
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One"))
    Assert.Throws<ArgumentException>(fun() -> box (TinyIntMapping.Parse("blah-blah"))) |> ignore
    Assert.Throws<ArgumentException>(fun() -> box (TinyIntMapping.Parse("one"))) |> ignore

[<Fact>]
let Enums() = 
    let succ, result = EnumMapping.TryParse("One")
    Assert.True succ
    Assert.Equal(EnumMapping.One, result)

    Assert.Equal(1, int EnumMapping.One)
    Assert.True(EnumMapping.One = (Enum.Parse(typeof<EnumMapping>, "One") |> unbox))
    Assert.Equal(enum 1, EnumMapping.One)

[<Fact>]
let Name() = 
    let value = TinyIntMapping.One
    Assert.Equal(Some "One", TinyIntMapping.TryFindName value)
    Assert.Equal(None, TinyIntMapping.TryFindName Byte.MinValue)

type SingleColumnSelect = SqlEnumProvider<"SELECT Name FROM Purchasing.ShipMethod", ConnectionStrings.AdventureWorksNamed>

[<Fact>]
let SingleColumn() =
    Assert.Equal<string>("CARGO TRANSPORT 5", SingleColumnSelect.``CARGO TRANSPORT 5``)
    let all = 
        use cmd = new SqlCommandProvider<"SELECT Name, Name FROM Purchasing.ShipMethod", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples>()
        cmd.Execute() |> Seq.toArray
    let items = SingleColumnSelect.Items
    Assert.Equal<_ seq>(all, items)

[<Fact>]
let PatternMatchingOn() =
    let actual = 
        SingleColumnSelect.Items
        |> Seq.choose (fun (tag, value) ->
            match value with
            | SingleColumnSelect.``CARGO TRANSPORT 5`` 
            | SingleColumnSelect.``OVERNIGHT J-FAST``
            | SingleColumnSelect.``OVERSEAS - DELUXE``
            | SingleColumnSelect.``XRQ - TRUCK GROUND``
            | SingleColumnSelect.``ZY - EXPRESS`` -> Some tag
            | _ -> None
        ) 

    Assert.Equal<_ seq>(
        SingleColumnSelect.Items |> Seq.map fst,
        actual
    )    

[<Fact>]
let MoreThan2ColumnReturnsCorrectTuples() =
    let actual = MoreThan2Columns.a
    Assert.Equal((1, "this is a"), actual)
    Assert.Equal<_ seq>(
      [ 
      ("a", (1, "this is a"))
      ("b", (2, "this is b"))
      ("c", (3, "this is c"))
      ]
      , MoreThan2Columns.Items
    )
