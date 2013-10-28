module FSharp.Data.SqlClient.TypeProviderTest

open Xunit

type QueryWithTinyInt = SqlCommand<"SELECT CAST(10 AS TINYINT) AS Value", ConnectionString="Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True", SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    let cmd = QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute())    
