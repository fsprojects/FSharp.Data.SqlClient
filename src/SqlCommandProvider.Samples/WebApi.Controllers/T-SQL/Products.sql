--uncomment for design time testing
--DECLARE 
--	@top AS BIGINT = 10, 
--	@SellStartDate AS DATETIME = '2002-06-01'

SELECT TOP (@top) Name AS ProductName, SellStartDate
FROM Production.Product
WHERE SellStartDate > @SellStartDate