module FSharp.Data.SqlClient.TypeProviderTest

open System
open System.Data
open Xunit

[<Literal>]
let connectionString = """Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True"""

type QueryWithTinyInt = SqlCommand<"SELECT CAST(10 AS TINYINT) AS Value", connectionString, SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    let cmd = QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute())    

type GetServerTime = SqlCommand<"IF @Bit = 1 SELECT 'TRUE' ELSE SELECT 'FALSE'", connectionString, SingleRow=true>

[<Fact>]
let SqlCommandClone() = 
    let cmd = new GetServerTime()
    Assert.Equal<string>("TRUE", cmd.Execute(Bit = 1))    
    let cmdClone = cmd.AsSqlCommand()
    cmdClone.Connection.Open()
    Assert.Throws<SqlClient.SqlException>(cmdClone.ExecuteScalar) |> ignore
    cmdClone.Parameters.["@Bit"].Value <- 1
    Assert.Equal(box "TRUE", cmdClone.ExecuteScalar())    
    Assert.Equal(cmdClone.ExecuteScalar(), cmd.Execute(Bit = 1))    
    Assert.Equal<string>("FALSE", cmd.Execute(Bit = 0))    
    Assert.Equal(box "TRUE", cmdClone.ExecuteScalar())    
    cmdClone.CommandText <- "SELECT 0"
    Assert.Equal<string>("TRUE", cmd.Execute(Bit = 1))    

type ConditionalQuery = SqlCommand<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", connectionString, SingleRow=true>

[<Fact>]
let ConditionalQuery() = 
    let cmd = ConditionalQuery()
    Assert.Equal((1, "monkey"), cmd.Execute(flag = 0))    
    Assert.Equal((2, "donkey"), cmd.Execute(flag = 1))    

// If compile fails here, check prereqs.sql
type TableValuedTuple  = SqlCommand<"exec myProc @x", connectionString, SingleRow = true>

[<Fact>]
let tableValuedTupleValue() = 
    let cmd = new TableValuedTuple()
    Assert.Equal((1, Some "monkey"), cmd.Execute(x = [ 1, Some "monkey" ; 2, Some "donkey" ]))    

[<Fact>] 
let tvpInputIsEnumeratedExactlyOnce() = 
    let cmd = new TableValuedTuple()
    let counter = ref 0
    let x = seq { 
         counter := !counter + 1
         yield 1, None
         yield 2, Some "donkey" }
    cmd.Execute x |> ignore
    Assert.Equal(1, !counter)    

[<Fact>] 
let tableValuedSprocTupleNull() = 
    let cmd = new TableValuedTuple()
    Assert.Equal((1, None), cmd.Execute([ 1, None ; 2, Some "donkey" ]))    


type TableValuedSingle = SqlCommand<"exec SingleElementProc @x", connectionString>

[<Fact>]
let tableValuedSingle() = 
    let cmd = new TableValuedSingle()
    let result = cmd.Execute(x = [ 1; 2 ]) |> List.ofSeq
    Assert.Equal<int list>([1;2], result)    

type TableValuedSprocTuple  = SqlCommand<"myProc", connectionString, SingleRow = true, CommandType = CommandType.StoredProcedure>

[<Fact>]
let tableValuedSprocTupleValue() = 
    let cmd = new TableValuedSprocTuple()
    let actual = cmd.Execute(p1 = [ 1, Some "monkey" ; 2, Some "donkey" ])
    Assert.Equal((1, Some "monkey"), actual)    

type ColumnsShouldNotBeNull2 = SqlCommand<"""SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'DatabaseLog' and numeric_precision is null
ORDER BY ORDINAL_POSITION""", connectionString, SingleRow = true>

[<Fact>]
let columnsShouldNotBeNull2() = 
    let cmd = new ColumnsShouldNotBeNull2()
    let _,_,_,_,precision = cmd.Execute()
    Assert.Equal(None, precision)    
