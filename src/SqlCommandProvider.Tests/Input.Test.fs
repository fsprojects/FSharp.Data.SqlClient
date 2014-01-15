module FSharp.Data.Experimental.InputTest

open System
open System.Data
open Xunit

//type QueryWithNullableParam = 
//    SqlCommand<"
//        declare @yCopy as int = @y
//        SELECT Result = @x + CASE WHEN @yCopy IS NULL THEN 1 ELSE @yCopy END
//    ",ConnectionStringName = "AdventureWorks2012", SingleRow = true, OptionalParameters = true>
//
//[<Fact>]
//let BothOptinalParamsSupplied() = 
//    let cmd = QueryWithNullableParam()
//    Assert.Equal(14, cmd.Execute(3, 11).Value)    
//
//[<Fact>]
//let SkipYParam() = 
//    let cmd = QueryWithNullableParam()
//    Assert.Equal(12, cmd.Execute(x = 11).Value)    
