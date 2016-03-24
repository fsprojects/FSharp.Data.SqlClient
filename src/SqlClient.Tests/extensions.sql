USE AdventureWorks2012

-- The following Sql must be run against AdventureWorks2012 for the tests to compile.
IF OBJECT_ID('dbo.AddRef') IS NOT NULL 
	DROP PROCEDURE dbo.AddRef;
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

--TYPES
IF TYPE_ID(N'dbo.MyTableType') IS NOT NULL
	DROP TYPE dbo.MyTableType
GO
IF TYPE_ID(N'Person.MyTableType') IS NOT NULL
	DROP TYPE Person.MyTableType
GO
IF TYPE_ID(N'dbo.u_int64') IS NOT NULL
	DROP TYPE dbo.u_int64
GO
IF TYPE_ID(N'dbo.SingleElementType') IS NOT NULL
	DROP TYPE dbo.SingleElementType 
GO
IF OBJECT_ID(N'dbo.TableHavingColumnNamesWithSpaces') IS NOT NULL
	DROP TABLE dbo.TableHavingColumnNamesWithSpaces
GO


CREATE PROCEDURE dbo.AddRef @x AS INT, @y AS INT, @sum AS INT OUTPUT 
AS
BEGIN
	SET @sum = @x + @y
	RETURN (@x + @y)
END
GO

CREATE TYPE dbo.MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO

CREATE PROCEDURE dbo.MyProc @p1 dbo.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO


CREATE TYPE Person.MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO


CREATE PROCEDURE Person.MyProc @p1 Person.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO

CREATE PROCEDURE Person.MyProc2 @p1 dbo.MyTableType readonly AS
BEGIN
   SELECT * from @p1 p
END

GO

CREATE TYPE dbo.SingleElementType AS TABLE (myId int not null)
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


CREATE TYPE dbo.u_int64 FROM NUMERIC (20) NOT NULL;
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

CREATE TABLE dbo.TableHavingColumnNamesWithSpaces (
    ID INT      IDENTITY (1, 1) NOT NULL,
    [Modified Date 2] DATETIME     DEFAULT (getdate()) NOT NULL,
);
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

