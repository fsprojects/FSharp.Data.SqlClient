namespace FSharp.Data

open System
open System.Configuration
open System.Transactions
open System.Data.SqlClient
open System.Data
open FSharp.Data
open Xunit
open FsUnit.Xunit

type Settings = FSharp.Configuration.AppSettings<"app.config">

type AdventureWorks = SqlProgrammabilityProvider<"name=AdventureWorks2012">

//Tables types structured as: [TypeAlias].[Namespace].Tables.[TableName]
type ShiftTable = AdventureWorks.HumanResources.Tables.Shift

type ResetIndentity = SqlCommandProvider<"DBCC CHECKIDENT ('HumanResources.Shift', RESEED, 4)", "name=AdventureWorks2012">
type GetRowCount = SqlCommandProvider<"SELECT COUNT(*) FROM HumanResources.Shift", "name=AdventureWorks2012", SingleRow = true>

type DataTablesTests() = 

    do
        use cmd = new ResetIndentity()
        cmd.Execute() |> ignore

    [<Fact>]
    member __.NewRowAndBulkCopy() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = Settings.ConnectionStrings.AdventureWorks2012)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        let rows: DataRow[] = 
            [|
                //erased method to provide static typing
                t.NewRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some DateTime.Now.Date)
                t.NewRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some DateTime.Now.Date)
            |]
        let bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tran)
        let rowsCopied = ref 0L
        bulkCopy.NotifyAfter <- rows.Length
        bulkCopy.SqlRowsCopied.Add(fun args -> rowsCopied := args.RowsCopied)
        //table name is there
        bulkCopy.DestinationTableName <- t.TableName
        bulkCopy.WriteToServer(rows)

        Assert.Equal(int64 rows.Length, !rowsCopied)

    [<Fact>]
    member __.AddRowAndBulkCopy() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = Settings.ConnectionStrings.AdventureWorks2012)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //erased method to provide static typing
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some DateTime.Now.Date)
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some DateTime.Now.Date)

        use getRowsCount = new GetRowCount(tran)
        let rowsBefore = getRowsCount.Execute().Value.Value
        
        //shortcut, convenience method
        t.BulkCopy(conn, SqlBulkCopyOptions.Default, tran)

        let rowsAdded = getRowsCount.Execute().Value.Value - rowsBefore
        Assert.Equal(t.Rows.Count, rowsAdded)

    [<Fact>]
    member __.DEFAULTConstraint() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = Settings.ConnectionStrings.AdventureWorks2012)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //ModifiedDate is not provided
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12.)
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16.)

        //remove ModifiedDate column therefore bulk insert won't send explicit NULLs to server
        t.Columns.Remove(t.ModifiedDateColumn)

        let bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tran)
        let rowsCopied = ref 0L
        bulkCopy.NotifyAfter <- t.Rows.Count
        bulkCopy.SqlRowsCopied.Add(fun args -> rowsCopied := args.RowsCopied)
        bulkCopy.DestinationTableName <- t.TableName
        bulkCopy.WriteToServer(t)

        Assert.Equal(int64 t.Rows.Count, !rowsCopied)

    [<Fact>]
    member __.DEFAULTConstraintInsertViaSqlDataAdapter() = 
        let t = new ShiftTable()
        Assert.True t.ModifiedDateColumn.AllowDBNull
        use conn = new SqlConnection(connectionString = Settings.ConnectionStrings.AdventureWorks2012)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //ModifiedDate is not provided
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12.)
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16.)

        //removing ModifiedDate column is not required as oppose to bulk insert 
        let rowsInserted = t.Update(conn, tran)
        Assert.Equal(t.Rows.Count, rowsInserted)










