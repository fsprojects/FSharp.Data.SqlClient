module FSharp.Data.SqlClient.Tests.EnumTests
#if USE_SYSTEM_DATA_COMMON_DBPROVIDERFACTORIES
open System
open Xunit
open FSharp.Data
open FSharp.Data.SqlClient
open FSharp.Data.SqlClient.Tests
type EnumMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", ConnectionStrings.LocalHost, Kind = SqlEnumKind.CLI>

[<Literal>]
let connectionString = ConnectionStrings.LocalHost

type TinyIntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", connectionString>

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

type MoreThan2Columns = SqlEnumProvider< @"
select * from (
values 
 ('a', 1, 'this is a')
 , ('b', 2, 'this is b')
 , ('c', 3, 'this is c')
) as v(code, id, description)
", connectionString>

[<Fact>]
let MoreThan2ColumnReturnsCorrectTuples() =
   Assert.Equal((1, "this is a"), MoreThan2Columns.a)
   Assert.Equal<_[]>(
     expected = [| 
         ("a", (1, "this is a"))
         ("b", (2, "this is b"))
         ("c", (3, "this is c"))
     |], 
     actual = Array.ofSeq MoreThan2Columns.Items
   )

type CurrencyCode = 
   SqlEnumProvider<"
       SELECT CurrencyCode
       FROM Sales.Currency 
       WHERE CurrencyCode IN ('USD', 'EUR', 'GBP')
   ", ConnectionStrings.AdventureWorksLiteral, Kind = SqlEnumKind.UnitsOfMeasure>

[<Fact>]
let ConvertUsdToGbp() =
   let getLatestRate = new SqlCommandProvider<"
       SELECT TOP 1 *
       FROM Sales.CurrencyRate
       WHERE FromCurrencyCode = @fromCurrency
	        AND ToCurrencyCode = @toCurrency 
       ORDER BY CurrencyRateDate DESC
       ", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
   let rate = 
       getLatestRate.Execute(fromCurrency = "USD", toCurrency = "GBP")
       |> Option.map(fun x -> x.AverageRate * 1M<CurrencyCode.GBP/CurrencyCode.USD>)
       |> Option.get

   let actual = 42M<CurrencyCode.USD> * rate
   let expected = 26.5986M<CurrencyCode.GBP>
   Assert.Equal( expected, actual)

type ProductsUnitsOfMeasure = SqlEnumProvider<"SELECT UnitMeasureCode FROM Production.UnitMeasure", ConnectionStrings.AdventureWorksLiteral, Kind = SqlEnumKind.UnitsOfMeasure>
type ProductCategory = SqlEnumProvider<"SELECT Name FROM Production.ProductCategory", ConnectionStrings.AdventureWorksLiteral>

type Bikes = {
   Id: int
   Name: string
   Weight: decimal<ProductsUnitsOfMeasure.``LB ``> option
   Size: float<ProductsUnitsOfMeasure.``CM ``> option
}

[<Fact>]
let ProductWeightAndSizeUnitsOfMeasure() =
   let allBikes = [
       use cmd = 
           new SqlCommandProvider<"
               SELECT ProductID, Product.Name, Size, SizeUnitMeasureCode, Weight, WeightUnitMeasureCode
               FROM Production.Product 
	                JOIN Production.ProductCategory ON ProductSubcategoryID = ProductCategoryID  
               WHERE ProductCategory.Name = @category
           ", ConnectionStrings.AdventureWorksNamed>()
                
       for x in cmd.Execute(ProductCategory.Bikes) do
           yield {
               Id = x.ProductID
               Name = x.Name
               Weight = x.Weight |> Option.map(fun weight -> weight * 1M<_>)
               Size = x.Size |> Option.map(fun size -> size |> float |> LanguagePrimitives.FloatWithMeasure )
           }
   ]

   let bigBikes = allBikes |> List.choose ( fun x -> if x.Size = Some 52.<ProductsUnitsOfMeasure.``CM ``> then Some x.Name else None)

   Assert.Equal<_ list>( ["Mountain-500 Silver, 52"; "Mountain-500 Black, 52"], bigBikes)
