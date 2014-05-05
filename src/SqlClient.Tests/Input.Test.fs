module FSharp.Data.InputTest

open System
open System.Data
open Xunit

type QueryWithNullableParam = 
    SqlCommandProvider<"declare @yCopy as int = @y
        SELECT Result = @x + ISNULL(@yCopy, 1)
    ","name=AdventureWorks2012", SingleRow = true, AllParametersOptional = true>

[<Fact>]
let BothOptinalParamsSupplied() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 14), cmd.Execute(Some 3, Some 11))    

[<Fact>]
let SkipYParam() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 12), cmd.Execute(x = Some 11))    
