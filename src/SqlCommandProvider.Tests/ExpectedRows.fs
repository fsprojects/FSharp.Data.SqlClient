module FSharp.Data.Experimental.Tests.ExpectedRows 

open FSharp.Data.Experimental
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type ResultTypeMapsWithNullableCols = 
    SqlCommand<"SELECT LastName FROM Person.Person WHERE BusinessEntityID=@id", "name=AdventureWorks2012", ResultRows = ExpectedRows.OneOrZero>

[<Fact>]
let ExpectedRowsOneOrZeroReturnsNone() = 
    let cmd = ResultTypeMapsWithNullableCols()
    let expected : string option = None
    let b = cmd.Execute -1
    Assert.Equal(expected, b)

[<Fact>]
let ExpectedRowsOneOrZeroReturnsSome() = 
    let cmd = ResultTypeMapsWithNullableCols()
    let expected = Some "Sánchez"
    let b = cmd.Execute 1
    Assert.Equal(expected, b)

    


