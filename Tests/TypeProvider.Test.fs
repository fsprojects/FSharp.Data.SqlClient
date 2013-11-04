module FSharp.Data.SqlClient.TypeProviderTest

open System
open Xunit

[<Literal>]
let connectionString = "Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type QueryWithTinyInt = SqlCommand<"SELECT CAST(10 AS TINYINT) AS Value", connectionString, SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    let cmd = QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute())    

type GetServerTime = SqlCommand<"IF @Bit = 1 SELECT 'TRUE' ELSE SELECT 'FALSE'", connectionString, SingleRow=true>

[<Fact>]
let sqlCommandClone() = 
    let cmd = new GetServerTime(Bit = 1)
    let cmdClone = cmd.AsSqlCommand()
    cmdClone.Connection.Open()
    Assert.Equal(cmdClone.ExecuteScalar(), cmd.Execute())    
    cmdClone.Parameters.["@Bit"].Value <- 0
    Assert.Equal<string>("TRUE", cmd.Execute())    
    Assert.Equal(box "FALSE", cmdClone.ExecuteScalar())    
    cmdClone.CommandText <- "SELECT 0"
    Assert.Equal<string>("TRUE", cmd.Execute())    

// If compile fails here, check prereqs.sql
type TableValuedTuple  = SqlCommand<"exec myProc @x", connectionString, SingleRow = true>
type TableValuedRecord = SqlCommand<"exec myProc @x", connectionString, ResultType = ResultType.Records, SingleRow = true>

[<Fact>]
let tableValuedSprocTupleValue() = 
    let cmd = new TableValuedTuple(x = [ 1, Some "monkey" ; 2, Some "donkey" ])
    Assert.Equal((1, Some "monkey"), cmd.Execute())    
    ()

[<Fact>] 
let tvpInputIsEnumeratedExactlyOnce() = 
    let cmd = new TableValuedTuple()
    let counter = ref 0
    cmd.x <- seq { 
         counter := !counter + 1
         yield 1, None
         yield 2, Some "donkey" }
    cmd.Execute() |> ignore
    Assert.Equal(1, !counter)    

[<Fact>] 
let tableValuedSprocTupleNull() = 
    let cmd = new TableValuedTuple()
    cmd.x <- [ 1, None ; 2, Some "donkey" ]
    Assert.Equal((1, None), cmd.Execute())    
    ()

