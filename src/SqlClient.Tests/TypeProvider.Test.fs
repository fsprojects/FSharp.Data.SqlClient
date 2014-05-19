module FSharp.Data.TypeProviderTest

open System
open System.Data
open System.Data.SqlClient
open Xunit
open FsUnit.Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type GetOddNumbers = SqlCommandProvider<"select * from (values (2), (4), (8), (24)) as T(value)", connectionString>

[<Fact>]
let asyncSinlgeColumn() = 
    Assert.Equal<int[]>([| 2; 4; 8;  24 |], (new GetOddNumbers()).AsyncExecute() |> Async.RunSynchronously |> Seq.toArray)    


[<Fact>]
let ConnectionClose() = 
    use cmd = new GetOddNumbers()
    let untypedCmd : SqlClient.ISqlCommand = upcast cmd
    let underlyingConnection = untypedCmd.Raw.Connection
    Assert.Equal(ConnectionState.Closed, underlyingConnection.State)
    Assert.Equal<int[]>([| 2; 4; 8;  24 |], cmd.Execute() |> Seq.toArray)    
    Assert.Equal(ConnectionState.Closed, underlyingConnection.State)

type QueryWithTinyInt = SqlCommandProvider<"SELECT CAST(10 AS TINYINT) AS Value", connectionString, SingleRow = true>

[<Fact>]
let TinyIntConversion() = 
    use cmd = new QueryWithTinyInt()
    Assert.Equal(Some 10uy, cmd.Execute().Value)    

type ConvertToBool = SqlCommandProvider<"IF @Bit = 1 SELECT 'TRUE' ELSE SELECT 'FALSE'", connectionString, SingleRow=true>

[<Fact>]
let SqlCommandClone() = 
    use cmd = new ConvertToBool()
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
    let cmd = new ConditionalQuery()
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

[<Fact>]
let asyncCustomRecord() =
    (new GetBitCoin()).AsyncExecute("USD") |> Async.RunSynchronously |> Seq.length |> should equal 1

type NoneSingleton = SqlCommandProvider<"select 1 where 1 = 0", connectionString, SingleRow = true>
type SomeSingleton = SqlCommandProvider<"select 1", connectionString, SingleRow = true>

[<Fact>]
let singleRowOption() =
    (new NoneSingleton()).Execute().IsNone |> should be True
    (new SomeSingleton()).AsyncExecute() |> Async.RunSynchronously |> should equal (Some 1)


type NullableStringInput = SqlCommandProvider<"select  ISNULL(@P1, '')", connectionString, SingleRow = true, AllParametersOptional = true>
type NullableStringInputStrict = SqlCommandProvider<"select  ISNULL(@P1, '')", connectionString, SingleRow = true>

open System.Data.SqlClient

[<Fact>]
let NullableStringInputParameter() = 
    (new NullableStringInput()).Execute(None) |> should equal (Some "")
    (new NullableStringInput()).Execute() |> should equal (Some "")
    (new NullableStringInputStrict()).Execute(null) |> should equal (Some "")
    (new NullableStringInput()).Execute(Some "boo") |> should equal (Some "boo")

type Echo = SqlCommandProvider<"SELECT CAST(@Date AS DATE), CAST(@Number AS INT)", connectionString, ResultType.Tuples>

[<Fact>]
let ToTraceString() =
    let now = DateTime.Now
    let num = 42
    let expected = sprintf "exec sp_executesql N'SELECT CAST(@Date AS DATE), CAST(@Number AS INT)',N'@Date Date,@Number Int',@Date='%A',@Number='%d'" now num
    let cmd = new Echo()
    cmd.ToTraceString( now, num) |> should equal expected

[<Fact>]
let ``ToTraceString for CRUD``() =    
    (new GetBitCoin()).ToTraceString(bitCoinCode) 
    |> should equal "exec sp_executesql N'SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code',N'@code NChar(3)',@code='BTC'"
    
    (new InsertBitCoin()).ToTraceString(bitCoinCode, bitCoinName) 
    |> should equal "exec sp_executesql N'INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())',N'@Code NChar(3),@Name NVarChar(7)',@Code='BTC',@Name='Bitcoin'"
    
    (new DeleteBitCoin()).ToTraceString(bitCoinCode) 
    |> should equal "exec sp_executesql N'DELETE FROM Sales.Currency WHERE CurrencyCode = @Code',N'@Code NChar(3)',@Code='BTC'"