-- The following Sql must be run against AdventureWorks2012 for the tests to compile.

DROP PROCEDURE MyProc
DROP PROCEDURE SingleElementProc
DROP TYPE MyTableType
DROP TYPE SingleElementType
GO

CREATE TYPE MyTableType AS TABLE (myId int not null, myName nvarchar(30) null)
GO

CREATE PROCEDURE MyProc @p1 MyTableType readonly AS
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
