USE AdventureWorks2014
-- The following Sql must be run against AdventureWorks2012 for the tests to compile.

DROP PROCEDURE Person.MyProc
DROP TYPE Person.MyTableType
DROP PROCEDURE SingleElementProc
DROP PROCEDURE [Init]
DROP PROCEDURE [Get]
DROP PROCEDURE [Swap]
DROP TYPE SingleElementType
IF OBJECT_ID('Person.Address_GetAddressBySpatialLocation') IS NOT NULL
BEGIN
	DROP PROCEDURE Person.Address_GetAddressBySpatialLocation;
END
GO

create procedure [dbo].[Swap]
	@input int = 4 ,
	@bitWithDefault bit = 1,
    @output int output,
	@nullOutput bit output,
	@nullStringOutput varchar output
as
begin
	set @output = @input
	return @input
end
go

CREATE TYPE dbo.MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
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


CREATE TYPE SingleElementType AS TABLE (myId int not null)
GO

CREATE PROCEDURE SingleElementProc @p1 SingleElementType readonly AS
BEGIN
   SELECT * from @p1 p
END
GO

create procedure [Init]
AS
begin
    exec sp_getapplock 
        @Resource = 'R',
        @LockMode = 'Exclusive',
        @LockOwner = 'Session',
        @LockTimeout = -1;
END
GO

create procedure [Get]
as
begin
    create table #result (id  uniqueidentifier not null)
    select * from #result
end


CREATE TYPE [dbo].[u_int64] FROM NUMERIC (20) NOT NULL;

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

CREATE FUNCTION [dbo].[ufnGetStock2](@ProductID [int] = NULL)
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
