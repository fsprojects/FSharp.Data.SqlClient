module FSharp.Data.OptionalParamsTests

open Xunit
open FsUnit.Xunit

[<Literal>]
let connectionString = ConnectionStrings.AdventureWorks

type QueryWithNullableParam = 
    SqlCommandProvider<"SELECT CAST(@x AS INT) + ISNULL(CAST(@y AS INT), 1)", connectionString, SingleRow = true, AllParametersOptional = true>

[<Fact>]
let BothOptinalParamsSupplied() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 14), cmd.Execute(Some 3, Some 11))    

[<Fact>]
let SkipYParam() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 12), cmd.Execute(x = Some 11))    

type NullableStringInput = SqlCommandProvider<"select ISNULL(CAST(@P1 AS VARCHAR), '')", connectionString, SingleRow = true, AllParametersOptional = true>
type NullableStringInputStrict = SqlCommandProvider<"select ISNULL(CAST(@P1 AS VARCHAR), '')", connectionString, SingleRow = true>

[<Fact>]
let NullableStringInputParameter() = 
    (new NullableStringInput()).Execute(None) |> should equal (Some "")
    (new NullableStringInput()).Execute() |> should equal (Some "")
    (new NullableStringInputStrict()).Execute(null) |> should equal (Some "")
    (new NullableStringInput()).Execute(Some "boo") |> should equal (Some "boo")