module FSharp.Data.SqlClient.TypeProviderTest

open System
open Xunit

[<Literal>]
let connectionString = "Data Source=.;Integrated Security=True"

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
