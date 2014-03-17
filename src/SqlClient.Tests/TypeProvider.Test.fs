module FSharp.Data.TypeProviderTest

open System
open System.Data
open System.Data.SqlClient
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type GetOddNumbers = SqlCommandProvider<"select * from (values (2), (4), (8), (24)) as T(value)", connectionString>

[<Fact>]
let ConnectionClose() = 
    let cmd = GetOddNumbers()
    let nativeCmd: SqlCommand = unbox cmd 
    Assert.Equal(ConnectionState.Closed, nativeCmd.Connection.State)
    Assert.Equal<int[]>([| 2; 4; 8;  24 |], cmd.Execute() |> Seq.toArray)    
    Assert.Equal(ConnectionState.Closed, nativeCmd.Connection.State)

type QueryWithTinyInt = SqlCommandProvider<"SELECT CAST(10 AS TINYINT) AS Value", connectionString, SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    let cmd = QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute().Value)    

type ConvertToBool = SqlCommandProvider<"IF @Bit = 1 SELECT 'TRUE' ELSE SELECT 'FALSE'", connectionString, SingleRow=true>

[<Fact>]
let SqlCommandClone() = 
    let cmd = new ConvertToBool()
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

type ConditionalQuery = SqlCommandProvider<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", connectionString, SingleRow=true, ResultType = ResultType.Tuples>

[<Fact>]
let ConditionalQuery() = 
    let cmd = ConditionalQuery()
    Assert.Equal(Some(1, "monkey"), cmd.Execute(flag = 0))    
    Assert.Equal(Some(2, "donkey"), cmd.Execute(flag = 1))    

type ColumnsShouldNotBeNull2 = 
    SqlCommandProvider<"SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
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

type DeleteBitCoin = SqlCommandProvider<"DELETE FROM Sales.Currency WHERE CurrencyCode = @Code", connectionString>
type InsertBitCoin = SqlCommandProvider<"INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())", connectionString>
type GetBitCoin = SqlCommandProvider<"SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code", connectionString>

open System.Transactions

[<Fact>]
let transaction() =
    DeleteBitCoin().Execute(bitCoinCode) |> ignore
    use conn = new System.Data.SqlClient.SqlConnection(connectionString)
    conn.Open()
    let tran = conn.BeginTransaction()
    Assert.Equal(1, InsertBitCoin(tran).Execute(bitCoinCode, bitCoinName))
    Assert.Equal(1, GetBitCoin(tran).Execute(bitCoinCode) |> Seq.length)
    tran.Rollback()
    Assert.Equal(0, GetBitCoin().Execute(bitCoinCode) |> Seq.length)

type NoneSingleton = SqlCommandProvider<"select 1 where 1 = 0", connectionString, SingleRow = true>
type SomeSingleton = SqlCommandProvider<"select 1", connectionString, SingleRow = true>

[<Fact>]
let singleRowOption() =
    Assert.True(NoneSingleton().Execute().IsNone)
    Assert.Equal(Some 1, SomeSingleton().Execute())


type NullableStringInput = SqlCommandProvider<"select  ISNULL(@P1, '')", connectionString, SingleRow = true, AllParametersOptional = true>
type NullableStringInputStrict = SqlCommandProvider<"select  ISNULL(@P1, '')", connectionString, SingleRow = true>

open System.Data.SqlClient

[<Fact>]
let NullableStringInputParameter() = 
    Assert.Equal(Some "", NullableStringInput().Execute())
    Assert.Equal(Some "", NullableStringInputStrict().Execute(null))


//     
//open Microsoft.SqlServer.Types
//
//type Spatial = SqlCommandProvider<"select top 5 SpatialLocation from Person.Address", connectionString>
//
//[<Fact>]
//let nativeTypes() =
//    let result = Spatial().Execute()
//    result |> Seq.iter (printfn "%A")


