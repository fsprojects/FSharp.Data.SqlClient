(*** hide ***)
#r "../../bin/SqlCommandTypeProvider.dll"

(**

Limitations
===================

Implementation uses metadata discovery stored procedures (sys.sp_describe_undeclared_parameters and sys.sp_describe_first_result_set) 
introduces in SQL Server 2012 and SQL Azure Database. Therefore it’s constrained by same limitation as those SPs. 
Among others are:

 * Requires SQL Server 2012 or SQL Azure Database at compile-time. SQL Compact is not supported.

 * Does not work with queries that use temporary tables

 * Parameters in a query may only be used once. You can work around this by declaring a local variable in Sql, and assigning the @param to that local variable:
    
    [lang=sql]
    DECLARE @input int
    SET @input = @param
    SELECT *
    FROM sys.indexes
    WHERE @input = 1 or @input = 2

Look online for more details on sys.sp_describe_undeclared_parameters and sys.sp_describe_first_result_set.

Additional constraints worthwhile to mention:

 * Stored procedures out parameters and return value are not supported 

 * This is "erased types" type provider which means it can be used in F# only

*)



