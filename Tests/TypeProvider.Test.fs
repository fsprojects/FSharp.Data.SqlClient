module FSharp.Data.SqlClient.TypeProviderTest

open System
open System.Data
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
let SqlCommandClone() = 
    let cmd = new GetServerTime(Bit = 1)
    let cmdClone = cmd.AsSqlCommand()
    cmdClone.Connection.Open()
    Assert.Equal(cmdClone.ExecuteScalar(), cmd.Execute())    
    cmdClone.Parameters.["@Bit"].Value <- 0
    Assert.Equal<string>("TRUE", cmd.Execute())    
    Assert.Equal(box "FALSE", cmdClone.ExecuteScalar())    
    cmdClone.CommandText <- "SELECT 0"
    Assert.Equal<string>("TRUE", cmd.Execute())    

type ConditionalQuery = SqlCommand<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", connectionString, SingleRow=true>

[<Fact>]
let ConditionalQuery() = 
    let cmd = ConditionalQuery(flag = 0)
    Assert.Equal((1, "monkey"), cmd.Execute())    
    cmd.flag <- 1
    Assert.Equal((2, "donkey"), cmd.Execute())    

// If compile fails here, check prereqs.sql
type TableValuedTuple  = SqlCommand<"exec myProc @x", connectionString, SingleRow = true>
type TableValuedSprocTuple  = SqlCommand<"myProc", connectionString, SingleRow = true, CommandType = CommandType.StoredProcedure>
type TableValuedSingle = SqlCommand<"exec SingleElementProc @x", connectionString>

[<Fact>]
let tableValuedTupleValue() = 
    let cmd = new TableValuedTuple(x = [ 1, Some "monkey" ; 2, Some "donkey" ])
    Assert.Equal((1, Some "monkey"), cmd.Execute())    
    ()

[<Fact>]
let tableValuedSprocTupleValue() = 
    let cmd = new TableValuedSprocTuple(p1 = [ 1, Some "monkey" ; 2, Some "donkey" ])
    Assert.Equal((1, Some "monkey"), cmd.Execute())    
    ()

[<Fact>]
let tableValuedSingle() = 
    let cmd = new TableValuedSingle(x = [ 1; 2 ])
    let result = cmd.Execute() |> List.ofSeq
    Assert.Equal<int list>([1;2], result)    
    ()

[<Fact>]
let tableValuedClone() = 
    let cmd = new TableValuedTuple(x = [ 1, Some "monkey" ; 2, Some "donkey" ])
    let clone = cmd.AsSqlCommand()

    Assert.Equal(1, clone.Parameters.Count)    
    let table = clone.Parameters.["@x"].Value :?> System.Data.DataTable
   
    Assert.Equal(1, Convert.ChangeType(table.Rows.[0].[0], typeof<int>) |> unbox)
    Assert.Equal<string>("donkey", Convert.ChangeType(table.Rows.[1].[1], typeof<string>) |> unbox)
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

