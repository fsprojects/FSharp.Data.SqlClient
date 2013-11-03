-- The following Sql must be run against AdventureWorks2012 for the tests to compile.

CREATE TYPE myTableType 
AS TABLE (myId int not null, myName nvarchar(30) null)

GO

CREATE PROCEDURE myProc 
   @p1 myTableType readonly
AS
BEGIN
	SELECT * from @p1 p
END

