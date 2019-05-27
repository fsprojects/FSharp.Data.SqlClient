-- The following Sql must be run against AdventureWorks2012 for the tests to compile.

USE AdventureWorks2012

--ROUTINES

IF OBJECT_ID('dbo.AddRef') IS NOT NULL 
	DROP PROCEDURE dbo.AddRef;
GO
IF OBJECT_ID('dbo.PassGuid') IS NOT NULL 
	DROP PROCEDURE dbo.PassGuid;
GO
IF OBJECT_ID('dbo.MyProc') IS NOT NULL
	DROP PROCEDURE dbo.MyProc;
GO
IF OBJECT_ID('Person.MyProc') IS NOT NULL
	DROP PROCEDURE Person.MyProc;
GO
IF OBJECT_ID('Person.MyProc2') IS NOT NULL
	DROP PROCEDURE Person.MyProc2;
GO
IF OBJECT_ID('dbo.SingleElementProc') IS NOT NULL
	DROP PROCEDURE dbo.SingleElementProc;
GO
IF OBJECT_ID('dbo.Init') IS NOT NULL
	DROP PROCEDURE dbo.[Init];
GO
IF OBJECT_ID('dbo.Get') IS NOT NULL
	DROP PROCEDURE dbo.[Get];
GO
IF OBJECT_ID('Person.Address_GetAddressBySpatialLocation') IS NOT NULL
	DROP PROCEDURE Person.Address_GetAddressBySpatialLocation;
GO
IF OBJECT_ID('dbo.ufnGetStock2') IS NOT NULL
	DROP FUNCTION dbo.ufnGetStock2;
GO
IF OBJECT_ID('dbo.Echo') IS NOT NULL
	DROP PROCEDURE dbo.Echo;
GO
IF OBJECT_ID('dbo.EchoText') IS NOT NULL
	DROP PROCEDURE dbo.EchoText;
GO
IF OBJECT_ID('dbo.MyFunc') IS NOT NULL
	DROP FUNCTION dbo.MyFunc;
GO
IF OBJECT_ID('dbo.HowMany') IS NOT NULL
	DROP FUNCTION dbo.HowMany;
GO
IF OBJECT_ID('dbo.HowManyRows') IS NOT NULL
	DROP PROCEDURE dbo.HowManyRows;
GO
IF OBJECT_ID('dbo.BinaryOutput') IS NOT NULL
	DROP PROCEDURE dbo.BinaryOutput;
GO
IF OBJECT_ID('dbo.TimestampOutput') IS NOT NULL
	DROP PROCEDURE dbo.TimestampOutput;
GO
IF OBJECT_ID('dbo.TestPhoto') IS NOT NULL
	DROP PROCEDURE dbo.TestPhoto;
GO
IF OBJECT_ID('Sales.GetUKSalesOrders') IS NOT NULL
	DROP FUNCTION Sales.GetUKSalesOrders;
GO

--TABLES

IF OBJECT_ID(N'dbo.TableHavingColumnNamesWithSpaces') IS NOT NULL
	DROP TABLE dbo.TableHavingColumnNamesWithSpaces
GO

IF OBJECT_ID(N'Sales.UnitedKingdomOrders') IS NOT NULL
	DROP TABLE Sales.UnitedKingdomOrders
GO

-- SYNONYMs

IF OBJECT_ID(N'HumanResources.GetContactInformation') IS NOT NULL
	DROP SYNONYM HumanResources.GetContactInformation
GO

IF OBJECT_ID(N'HumanResources.GetEmployeeManagers') IS NOT NULL
	DROP SYNONYM HumanResources.GetEmployeeManagers
GO

IF OBJECT_ID(N'dbo.HRShift') IS NOT NULL
	DROP SYNONYM dbo.HRShift
GO

--TYPES
IF TYPE_ID(N'dbo.MyTableType') IS NOT NULL
	DROP TYPE dbo.MyTableType
GO
IF TYPE_ID(N'Person.MyTableType') IS NOT NULL
	DROP TYPE Person.MyTableType
GO
IF TYPE_ID(N'dbo.MyTableTypeFixed') IS NOT NULL
	DROP TYPE dbo.MyTableTypeFixed
GO
IF TYPE_ID(N'dbo.u_int64') IS NOT NULL
	DROP TYPE dbo.u_int64
GO
IF TYPE_ID(N'dbo.SingleElementType') IS NOT NULL
	DROP TYPE dbo.SingleElementType 
GO

IF TYPE_ID(N'Sales.<GBP>') IS NOT NULL
	DROP TYPE Sales.[<GBP>]
GO

IF TYPE_ID(N'Sales.<USD>') IS NOT NULL
	DROP TYPE Sales.[<USD>]
GO


CREATE TYPE dbo.MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO

CREATE TYPE Person.MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO

CREATE TYPE dbo.MyTableTypeFixed AS TABLE (myId int not null, myName nchar(30) null)
GO

CREATE TYPE dbo.SingleElementType AS TABLE (myId int not null)
GO

CREATE TYPE dbo.u_int64 FROM NUMERIC (20) NOT NULL;
GO

CREATE TYPE Sales.[<GBP>] FROM MONEY NOT NULL
GO

CREATE TYPE Sales.[<USD>] FROM MONEY NOT NULL
GO

--TABLES

CREATE TABLE dbo.TableHavingColumnNamesWithSpaces (
    ID INT      IDENTITY (1, 1) NOT NULL,
    [Modified Date 2] DATETIME     DEFAULT (getdate()) NOT NULL,
);
GO

CREATE TABLE Sales.UnitedKingdomOrders(
	[SalesOrderID] [int] NOT NULL,
	[TotalDue] [Sales].[<GBP>] NOT NULL
)
GO 

INSERT INTO Sales.UnitedKingdomOrders
SELECT SalesOrderID, TotalDue
FROM Sales.SalesOrderHeader X
	JOIN Sales.CurrencyRate Y ON 
		X.CurrencyRateID = Y.CurrencyRateID
		AND Y.ToCurrencyCode = 'GBP'

GO 

--ROUTINES

CREATE PROCEDURE Person.MyProc @p1 Person.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO

CREATE PROCEDURE dbo.AddRef @x AS INT, @y AS INT, @sum AS INT OUTPUT 
AS
BEGIN
	SET @sum = @x + @y
	RETURN (@x + @y)
END
GO

CREATE PROCEDURE dbo.PassGuid @x AS UNIQUEIDENTIFIER, @b AS BIT, @result AS UNIQUEIDENTIFIER OUTPUT 
AS
BEGIN
	IF (@b = 1)
	BEGIN
		SET @result = @x 
	END
END
GO

CREATE PROCEDURE dbo.MyProc @p1 dbo.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO

CREATE PROCEDURE Person.MyProc2 @p1 dbo.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END

GO

CREATE PROCEDURE dbo.SingleElementProc @p1 SingleElementType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO

CREATE PROCEDURE dbo.[Init]
AS
BEGIN
    EXEC sp_getapplock 
        @Resource = 'R',
        @LockMode = 'Exclusive',
        @LockOwner = 'Session',
        @LockTimeout = -1;
END
GO

CREATE PROCEDURE dbo.[Get]
AS
BEGIN
    CREATE TABLE #result (id  UNIQUEIDENTIFIER not null)
    SELECT * FROM #result
END
GO

CREATE PROCEDURE Person.Address_GetAddressBySpatialLocation
	@SpatialLocation GEOGRAPHY
AS
SELECT
	AddressLine1,
	City,
	SpatialLocation
FROM Person.[Address]
WHERE
	SpatialLocation.STDistance(@SpatialLocation) = 0;
GO


CREATE FUNCTION dbo.ufnGetStock2(@ProductID [int] = NULL)
RETURNS [int] 
AS 
-- Returns the stock level for the product. This function is used internally only
BEGIN
    DECLARE @ret int;
    
    SELECT @ret = SUM(p.[Quantity]) 
    FROM [Production].[ProductInventory] p 
    WHERE 
		(@ProductID IS NULL OR p.[ProductID] = @ProductID) 
        AND p.[LocationID] = '6'; -- Only look at inventory in the misc storage
    
    IF (@ret IS NULL) 
        SET @ret = 0
    
    RETURN @ret
END;
GO


CREATE PROCEDURE dbo.Echo(@in SQL_VARIANT = 'Empty')
AS
	SELECT @in;
GO


CREATE PROCEDURE dbo.EchoText(@in VARCHAR(max) = NULL)
AS
	SELECT ISNULL(@in, '<NULL>');
GO

CREATE FUNCTION dbo.MyFunc(@p1 dbo.MyTableType readonly, @p2 dbo.MyTableType readonly)
RETURNS TABLE 
RETURN (SELECT * from @p1 UNION SELECT * from @p2) 
GO

CREATE PROCEDURE dbo.HowManyRows @p1 dbo.MyTableType READONLY, @total AS BIGINT OUTPUT AS
BEGIN
	SET @total = (SELECT COUNT_BIG(*) FROM @p1)
	SELECT myId, myName FROM @p1 WHERE myName IS NOT NULL
END

GO

CREATE PROCEDURE dbo.BinaryOutput @out AS BINARY(5) OUTPUT AS
BEGIN
	SELECT @out = CAST(42 AS BINARY(5)); 
END

GO

CREATE PROCEDURE dbo.TimestampOutput @timestamp TIMESTAMP OUTPUT AS
BEGIN
	SELECT @timestamp = CAST(42 AS TIMESTAMP); 
END

GO

CREATE PROCEDURE dbo.TestPhoto
    -- Add the parameters for the stored procedure here
	@id int
    ,@img varbinary(max)
AS
BEGIN
    -- SET NOCOUNT ON added to prevent extra result sets from
    -- interfering with SELECT statements.
    SET NOCOUNT ON;
    -- Insert statements for procedure here
    SET IDENTITY_INSERT Production.ProductPhoto ON
    INSERT INTO Production.ProductPhoto (ProductPhotoId, LargePhoto) 
    OUTPUT inserted.ProductPhotoId, inserted.LargePhoto
    VALUES (@id, @img)
    SET IDENTITY_INSERT Production.ProductPhoto OFF
END
GO

CREATE FUNCTION Sales.GetUKSalesOrders(@min AS Sales.[<GBP>])
RETURNS TABLE 
RETURN 
    SELECT 
	    Total = SUM(x.TotalDue)
	    ,[Year] = DATEPART(year, y.OrderDate)
    FROM Sales.UnitedKingdomOrders x
	    JOIN Sales.SalesOrderHeader y on x.SalesOrderID = y.SalesOrderID
    GROUP BY DATEPART(year, y.OrderDate)
    HAVING SUM(x.TotalDue) > @min
GO


GO 
--SYNONYMS

CREATE SYNONYM HumanResources.GetContactInformation FOR dbo.ufnGetContactInformation;
GO

CREATE SYNONYM HumanResources.GetEmployeeManagers FOR dbo.uspGetEmployeeManagers;
GO

CREATE SYNONYM dbo.HRShift FOR HumanResources.Shift
GO

