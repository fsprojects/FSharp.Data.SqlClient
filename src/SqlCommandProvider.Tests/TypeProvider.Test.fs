module FSharp.Data.Experimental.TypeProviderTest

open System
open System.Data
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type QueryWithTinyInt = SqlCommand<"SELECT CAST(10 AS TINYINT) AS Value", connectionString, SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    let cmd = QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute().Value)    

type GetServerTime = SqlCommand<"IF @Bit = 1 SELECT 'TRUE' ELSE SELECT 'FALSE'", connectionString, SingleRow=true>

[<Fact>]
let SqlCommandClone() = 
    let cmd = new GetServerTime()
    Assert.Equal(Some "TRUE", cmd.Execute(Bit = 1))    
    let cmdClone = cmd.AsSqlCommand()
    cmdClone.Connection.Open()
    Assert.Throws<SqlClient.SqlException>(cmdClone.ExecuteScalar) |> ignore
    cmdClone.Parameters.["@Bit"].Value <- 1
    Assert.Equal(box "TRUE", cmdClone.ExecuteScalar())    
    Assert.Equal(cmdClone.ExecuteScalar(), cmd.Execute(Bit = 1).Value)    
    Assert.Equal(Some "FALSE", cmd.Execute(Bit = 0))    
    Assert.Equal(box "TRUE", cmdClone.ExecuteScalar())    
    cmdClone.CommandText <- "SELECT 0"
    Assert.Equal(Some "TRUE", cmd.Execute(Bit = 1))    

type ConditionalQuery = SqlCommand<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", connectionString, SingleRow=true, ResultType = ResultType.Tuples>

[<Fact>]
let ConditionalQuery() = 
    let cmd = ConditionalQuery()
    Assert.Equal(Some(1, "monkey"), cmd.Execute(flag = 0))    
    Assert.Equal(Some(2, "donkey"), cmd.Execute(flag = 1))    

type ColumnsShouldNotBeNull2 = 
    SqlCommand<"SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'DatabaseLog' and numeric_precision is null
            ORDER BY ORDINAL_POSITION", connectionString, SingleRow = true, ResultType = ResultType.Tuples>

[<Fact>]
let columnsShouldNotBeNull2() = 
    let cmd = new ColumnsShouldNotBeNull2()
    let _,_,_,_,precision = cmd.Execute() |> Option.get
    Assert.Equal(None, precision)    

[<Literal>]
let bitCoinCode = "BTC"
[<Literal>]
let bitCoinName = "Bitcoin"

type DeleteBitCoin = SqlCommand<"DELETE FROM Sales.Currency WHERE CurrencyCode = @Code", connectionString>
type InsertBitCoin = SqlCommand<"INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())", connectionString>
type GetBitCoin = SqlCommand<"SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code", connectionString>

open System.Transactions

[<Fact>]
let transactionScope() =
    DeleteBitCoin().Execute(bitCoinCode) |> ignore
    use conn = new System.Data.SqlClient.SqlConnection(connectionString)
    conn.Open()
    let tran = conn.BeginTransaction()
    Assert.Equal(1, InsertBitCoin(tran).Execute(bitCoinCode, bitCoinName))
    Assert.Equal(1, GetBitCoin(tran).Execute(bitCoinCode) |> Seq.length)
    tran.Rollback()
    Assert.Equal(0, GetBitCoin().Execute(bitCoinCode) |> Seq.length)

type NoneSingleton = SqlCommand<"select 1 where 1 = 0", connectionString, SingleRow = true>
type SomeSingleton = SqlCommand<"select 1", connectionString, SingleRow = true>

[<Fact>]
let singleRowOption() =
    Assert.True(NoneSingleton().Execute().IsNone)
    Assert.Equal(Some 1, SomeSingleton().Execute())
     


