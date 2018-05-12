module FSharp.Data.TypeProviderTest

#if DEBUG
#nowarn "101"
#endif

open System
open System.Data
open System.Data.SqlClient
open Xunit
   
type GetEvenNumbers = SqlCommandProvider<"select * from (values (2), (4), (8), (24)) as T(value)", ConnectionStrings.AdventureWorksNamed>

[<Fact>]
let asyncSinlgeColumn() = 
    use cmd = new GetEvenNumbers()
    Assert.Equal<int[]>([| 2; 4; 8; 24 |], cmd.AsyncExecute() |> Async.RunSynchronously |> Seq.toArray)    

[<Fact>]
let emptyResultset() = 
    use cmd = new SqlCommandProvider<"SELECT 42 WHERE 0 > 1", ConnectionStrings.AdventureWorksNamed>()
    Assert.Equal<_ []>( Array.empty, cmd.Execute() |> Seq.toArray)    

[<Fact>]
let ConnectionClose() = 
    use cmd = new GetEvenNumbers()
    let untypedCmd : ISqlCommand = upcast cmd
    let underlyingConnection = untypedCmd.Raw.Connection
    Assert.Equal(ConnectionState.Closed, underlyingConnection.State)
    Assert.Equal<int[]>([| 2; 4; 8;  24 |], cmd.Execute() |> Seq.toArray)    
    Assert.Equal(ConnectionState.Closed, underlyingConnection.State)

[<Fact>]
let ExternalInstanceConnection() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorks)
    conn.Open()
    use cmd = new GetEvenNumbers(conn)
    let untypedCmd : ISqlCommand = upcast cmd
    let underlyingConnection = untypedCmd.Raw.Connection
    Assert.Equal(ConnectionState.Open, underlyingConnection.State)
    Assert.Equal<int[]>([| 2; 4; 8;  24 |], cmd.Execute() |> Seq.toArray)    
    Assert.Equal(ConnectionState.Open, underlyingConnection.State)


[<Fact>]
let TinyIntConversion() = 
    use cmd = new SqlCommandProvider<"SELECT CAST(10 AS TINYINT) AS Value", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
    Assert.Equal(Some 10uy, cmd.Execute().Value)    

[<Fact>]
let ConditionalQuery() = 
    use cmd = new SqlCommandProvider<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", ConnectionStrings.AdventureWorksNamed, SingleRow=true, ResultType = ResultType.Tuples>()
    Assert.Equal(Some(1, "monkey"), cmd.Execute(flag = 0))    
    Assert.Equal(Some(2, "donkey"), cmd.Execute(flag = 1))    

[<Fact>]
let columnsShouldNotBeNull2() = 
    use cmd = new SqlCommandProvider<"
        SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'DatabaseLog' and numeric_precision is null
        ORDER BY ORDINAL_POSITION
    ", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples, SingleRow = true>()

    let _,_,_,_,precision = cmd.Execute().Value
    Assert.Equal(None, precision)    

[<Literal>]
let bitCoinCode = "BTC"
[<Literal>]
let bitCoinName = "Bitcoin"

type DeleteBitCoin = SqlCommandProvider<"DELETE FROM Sales.Currency WHERE CurrencyCode = @Code", ConnectionStrings.AdventureWorksNamed>
type InsertBitCoin = SqlCommandProvider<"INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())", ConnectionStrings.AdventureWorksNamed>
type GetBitCoin = SqlCommandProvider<"SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code", ConnectionStrings.AdventureWorksNamed>

[<Fact>]
let asyncCustomRecord() =
    use cmd = new GetBitCoin()
    Assert.Equal(
        1,
        cmd.AsyncExecute("USD") |> Async.RunSynchronously |> Seq.length
    )

[<Fact>]
let singleRowOption() =
    use noneSingleton = new SqlCommandProvider<"select 1 where 1 = 0", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
    Assert.IsNone <| noneSingleton.Execute()

    use someSingleton = new SqlCommandProvider<"select 1", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
    Assert.Equal( Some 1, someSingleton.AsyncExecute() |> Async.RunSynchronously)

[<Fact>]
let ToTraceString() =
    let now = DateTime.Now
    let num = 42
    let expected = sprintf "exec sp_executesql N'SELECT CAST(@Date AS DATE), CAST(@Number AS INT)',N'@Date Date,@Number Int',@Date='%A',@Number='%d'" now num
    let cmd = new SqlCommandProvider<"SELECT CAST(@Date AS DATE), CAST(@Number AS INT)", ConnectionStrings.AdventureWorksNamed, ResultType.Tuples>()
    Assert.Equal<string>(
        expected, 
        actual = cmd.ToTraceString( now, num)
    )

[<Fact>]
let ``ToTraceString for CRUD``() =    

    Assert.Equal<string>(
        expected = "exec sp_executesql N'SELECT CurrencyCode, Name FROM Sales.Currency WHERE CurrencyCode = @code',N'@code NChar(3)',@code='BTC'",
        actual = let cmd = new GetBitCoin() in cmd.ToTraceString( bitCoinCode)
    )
    
    Assert.Equal<string>(
        expected = "exec sp_executesql N'INSERT INTO Sales.Currency VALUES(@Code, @Name, GETDATE())',N'@Code NChar(3),@Name NVarChar(50)',@Code='BTC',@Name='Bitcoin'",
        actual = let cmd = new InsertBitCoin() in cmd.ToTraceString( bitCoinCode, bitCoinName)
    )

    Assert.Equal<string>(
        expected = "exec sp_executesql N'DELETE FROM Sales.Currency WHERE CurrencyCode = @Code',N'@Code NChar(3)',@Code='BTC'",
        actual = let cmd = new DeleteBitCoin() in cmd.ToTraceString( bitCoinCode)
    )
    
[<Fact>]
let ``ToTraceString double-quotes``() =    
    use cmd = new SqlCommandProvider<"SELECT OBJECT_ID('Sales.Currency')", ConnectionStrings.AdventureWorksNamed>()
    let trace = cmd.ToTraceString()
    Assert.Equal<string>("exec sp_executesql N'SELECT OBJECT_ID(''Sales.Currency'')'", trace)

[<Fact(
    Skip = "Don't execute for usual runs. Too slow."
    )>]
let CommandTimeout() =
    use cmd = 
        new SqlCommandProvider<"WAITFOR DELAY '00:00:06'; SELECT 42", ConnectionStrings.AdventureWorksNamed, SingleRow = true>(commandTimeout = 60)
    Assert.Equal(60, cmd.CommandTimeout)
    Assert.Equal(Some 42, cmd.Execute())     

[<Fact>]
let DynamicSql() =    
    let cmd = new SqlCommandProvider<"
	    DECLARE @stmt AS NVARCHAR(MAX) = @tsql
	    DECLARE @params AS NVARCHAR(MAX) = N'@p1 nvarchar(100)'
	    DECLARE @p1 AS NVARCHAR(100) = @firstName
	    EXECUTE sp_executesql @stmt, @params, @p1
	    WITH RESULT SETS
	    (
		    (
			    Name NVARCHAR(100)
			    ,UUID UNIQUEIDENTIFIER 
		    )
	    )
    ", ConnectionStrings.AdventureWorksNamed>()
    //provide dynamic sql query with param
    Assert.Equal(
        51,
        cmd.Execute("SELECT CONCAT(FirstName, LastName) AS Name, rowguid AS UUID FROM Person.Person WHERE FirstName = @p1", "Alex") |> Seq.toArray |> Array.length
    )
    //extend where condition by filetering out additional rows
    Assert. Equal(
        9,
        cmd.Execute("SELECT CONCAT(FirstName, LastName) AS Name, rowguid AS UUID FROM Person.Person WHERE FirstName = @p1 AND EmailPromotion = 2", "Alex") |> Seq.toArray |> Array.length
    )
    //accessing completely diff table
    Assert.Equal(
        1,
        cmd.Execute("SELECT Name, rowguid AS UUID FROM Production.Product WHERE Name = @p1", "Chainring Nut") |> Seq.toArray |> Array.length
    )

[<Fact>]
let DeleteStatement() =    
    use cmd = new SqlCommandProvider<"
        DECLARE @myTable TABLE( id INT)
        INSERT INTO @myTable VALUES (42)
        DELETE FROM @myTable
        ", ConnectionStrings.AdventureWorksNamed>(ConnectionStrings.AdventureWorks)
    Assert.Equal(2, cmd.Execute())

type GetDate = SqlCommandProvider<"select getdate()", ConnectionStrings.AdventureWorksNamed>
[<Fact>]
let ``Setting the command timeout isn't overridden when giving ConnectionStrings.AdventureWorksNamed context``() =
    let customTimeout = (Random()).Next(512, 1024)
    let getDate = new GetDate(commandTimeout = customTimeout)
    Assert.Equal(customTimeout, getDate.CommandTimeout)
    let sqlCommand = (getDate :> ISqlCommand).Raw
    Assert.Equal(customTimeout, sqlCommand.CommandTimeout)

    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    let getDate2 = new GetDate(conn, commandTimeout = customTimeout)
    Assert.Equal(customTimeout, getDate2.CommandTimeout)
    let sqlCommand = (getDate2 :> ISqlCommand).Raw
    Assert.Equal(customTimeout, sqlCommand.CommandTimeout)

[<Fact(Skip = "Thread safe execution is not supported yet")>]
let ConcurrentReaders() =
    let cmd = new GetEvenNumbers(ConnectionStrings.AdventureWorksLiteralMultipleActiveResults)
    let expected  = [| 2, 2; 4,4; 8,8; 24, 24 |]
    let actual = (cmd.Execute(), cmd.Execute()) ||> Seq.zip |> Seq.toArray
    Assert.Equal<_[]>(expected, actual)

[<Fact>]
let ResultsetExtendedWithTrailingColumn() =
    let cmd = new SqlCommandProvider<"
        WITH XS AS
        (
	        SELECT 
                Value
                ,GETDATE() AS Now
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    ", ConnectionStrings.AdventureWorksNamed>()

    Assert.Equal<_ list>([0..9], [ for x in cmd.Execute() -> x.Value ])
    
    (cmd :> ISqlCommand).Raw.CommandText <-"
        WITH XS AS
        (
	        SELECT 
                Value
                ,GETDATE() AS Now
	            ,SUM(Value) OVER (ORDER BY Value) AS Total
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    "
    Assert.Equal<_ list>([0..9], [ for x in cmd.Execute() -> x.Value ])

open FSharp.Data.SqlClient

[<Fact>]
let ResultsetRuntimeVerificationLessThanExpectedColumns() =
    let cmd = new SqlCommandProvider<"
        WITH XS AS
        (
	        SELECT 
                Value
                ,GETDATE() AS Now
	            ,SUM(Value) OVER (ORDER BY Value) AS Total
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    ", ConnectionStrings.AdventureWorksNamed>()

    Assert.Equal<_ list>([0..9], [ for x in cmd.Execute() -> x.Value ])
    
    (cmd :> ISqlCommand).Raw.CommandText <-"
        WITH XS AS
        (
	        SELECT 
                Value
                ,GETDATE() AS Now
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    "

    Assert.False(SqlClient.Configuration.Current.ResultsetRuntimeVerification)

    try
        SqlClient.Configuration.Current <- { ResultsetRuntimeVerification = true }
        let err = Assert.Throws<InvalidOperationException>(fun() -> cmd.Execute() |> Seq.toArray |> ignore)    
        Assert.Equal<string>(
            "Expected at least 3 columns in result set but received only 2.",
            err.Message
        )
    finally 
        SqlClient.Configuration.Current <- { ResultsetRuntimeVerification = false}

    let err = Assert.Throws<IndexOutOfRangeException>(fun() -> cmd.Execute() |> Seq.toArray |> ignore)    
    Assert.Equal<string>(
        "Index was outside the bounds of the array.",
        err.Message
    )


[<Fact>]
let ResultsetRuntimeVerificationDiffColumnTypes() =
    let cmd = new SqlCommandProvider<"
        WITH XS AS
        (
	        SELECT 
                Value
	            ,SUM(Value) OVER (ORDER BY Value) AS Total
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    ", ConnectionStrings.AdventureWorksNamed>()

    Assert.Equal<_ list>([0..9], [ for x in cmd.Execute() -> x.Value ])
    
    (cmd :> ISqlCommand).Raw.CommandText <-"
        WITH XS AS
        (
	        SELECT 
                Value
                ,GETDATE() AS Now
	        FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) as T(Value)
        )
        SELECT * FROM XS
    "

    Assert.False(Configuration.Current.ResultsetRuntimeVerification)

    try
        Configuration.Current <- { ResultsetRuntimeVerification = true }

        let err = Assert.Throws<InvalidOperationException>(fun() -> cmd.Execute() |> Seq.toArray |> ignore)    
        Assert.Equal<string>(
            """Expected column [Total] of type "System.Int32" at position 1 (0-based indexing) but received column [Now] of type "System.DateTime".""",
            err.Message
        )
    finally 
        Configuration.Current <- { ResultsetRuntimeVerification = false}

    let err = Assert.Throws<InvalidCastException>(fun() -> cmd.Execute() |> Seq.toArray |> ignore)    
    Assert.Equal<string>(
        "Specified cast is not valid.",
        err.Message
    )

