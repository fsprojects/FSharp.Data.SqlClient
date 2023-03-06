namespace FSharp.Data

open System
open System.Configuration
open System.Transactions
open Microsoft.Data.SqlClient
open System.Data
open FSharp.Data
open FSharp.Data.SqlClient
open Xunit

open ProgrammabilityTest

//Tables types structured as: [TypeAlias].[Namespace].Tables.[TableName]
type ShiftTable = AdventureWorks.HumanResources.Tables.Shift
type ProductCostHistory = AdventureWorks.Production.Tables.ProductCostHistory

type GetRowCount = SqlCommandProvider<"SELECT COUNT(*) FROM HumanResources.Shift", ConnectionStrings.AdventureWorksNamed, SingleRow = true>
type GetShiftTableData = SqlCommandProvider<"SELECT * FROM HumanResources.Shift", ConnectionStrings.AdventureWorksNamed, ResultType.DataReader>
type GetArbitraryDataAsDataTable = SqlCommandProvider<"select 1 a, 2 b, 3 c, cast(null as int) d", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>

type DataTablesTests() = 

    do
        use cmd = new SqlCommandProvider<"DBCC CHECKIDENT ('HumanResources.Shift', RESEED, 4)", ConnectionStrings.AdventureWorksNamed>()
        cmd.Execute() |> ignore

    let adventureWorks = ConfigurationManager.ConnectionStrings.["AdventureWorks"].ConnectionString
    
    [<Fact>]
    member __.NewRowAndBulkCopy() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = adventureWorks)
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

    [<Fact
        //(Skip="")
    >]
    member __.AddRowAndBulkCopy() = 
        try
            let t = new ShiftTable()
    
            //erased method to provide static typing
            let now = DateTime.Now.Date
            t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some now)
            t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some now)
            //check type. Should DateTime not option<DateTime>
            Assert.Equal<DateTime>(now, t.Rows.[0].ModifiedDate)

            use getRowsCount = new GetRowCount()
            let rowsBefore = getRowsCount.Execute().Value.Value
        
            //shortcut, convenience method
            t.BulkCopy()

            let rowsAdded = getRowsCount.Execute().Value.Value - rowsBefore
            Assert.Equal(t.Rows.Count, rowsAdded)
        finally
            //compenstating tran
            use cmd = new SqlCommandProvider<"
                DELETE FROM HumanResources.Shift WHERE Name IN ('French coffee break', 'Spanish siesta')
            ", ConnectionStrings.AdventureWorksNamed>()
            cmd.Execute() |> ignore

    [<Fact>]
    member __.AddRowAndBulkCopyWithConnOverride() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //erased method to provide static typing
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some DateTime.Now.Date)
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some DateTime.Now.Date)

        use getRowsCount = new GetRowCount(transaction = tran)
        let rowsBefore = getRowsCount.Execute().Value.Value
        
        //shortcut, convenience method
        t.BulkCopy(conn, SqlBulkCopyOptions.Default, tran)

        let rowsAdded = getRowsCount.Execute().Value.Value - rowsBefore
        Assert.Equal(t.Rows.Count, rowsAdded)

    [<Fact>]
    member __.DEFAULTConstraint() = 
        let t = new ShiftTable()
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //ModifiedDate is not provided
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12.)
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16.)

        //remove ModifiedDate column therefore bulk insert won't send explicit NULLs to server
        t.Columns.Remove(t.Columns.ModifiedDate)

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
        Assert.True t.Columns.ModifiedDate.AllowDBNull
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        //ModifiedDate is not provided
        t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12.)
        let yesterday = DateTime.Today.AddDays -1.
        t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., ModifiedDate = Some yesterday)

        //removing ModifiedDate column is not required as oppose to bulk insert 
        let rowsInserted = t.Update(conn, tran)
        let latestIdentity = 
            use cmd = new SqlCommandProvider<"SELECT IDENT_CURRENT (@tableName)", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
            cmd.Execute( t.TableName) |> Option.get |> Option.get |> Convert.ToByte

        Assert.Equal(t.Rows.Count, rowsInserted)

        //identity values retrived
        Assert.Equal(t.Rows.[1].ShiftID, latestIdentity)
        Assert.Equal(t.Rows.[0].ShiftID, latestIdentity - 1uy)

        //default values
        Assert.Equal(t.Rows.[1].ModifiedDate, yesterday)
        let serverDate = //because Azure in UTC
            use cmd = new SqlCommandProvider<"SELECT GetDate()", ConnectionStrings.AdventureWorksNamed, SingleRow = true>()
            cmd.Execute().Value
        Assert.Equal(t.Rows.[0].ModifiedDate.Date, serverDate.Date)

    [<Fact>]
    member __.UpdatesPlusAmbientTransaction() = 
        
        use tran = new TransactionScope()
            
        let t = new ShiftTable()
        use getShiftTableData = new GetShiftTableData()
        getShiftTableData.Execute() |> t.Load

        let eveningShift = t.Rows |> Seq.find (fun row -> row.Name = "Evening")
        let finishBy10 = TimeSpan(22, 0, 0)
        Assert.NotEqual(finishBy10, eveningShift.EndTime)
        eveningShift.EndTime <- finishBy10
    
        let rowsUpdated = t.Update()
        Assert.Equal(1, rowsUpdated)

        use getShift = new SqlCommandProvider<"SELECT * FROM HumanResources.Shift", ConnectionStrings.AdventureWorksNamed>()
        let eveningShiftInDb = getShift.Execute() |> Seq.find (fun x -> x.Name = "Evening")
        Assert.Equal(finishBy10, eveningShiftInDb.EndTime)

    [<Fact>]
    member __.TableTypeTag() = 
        Assert.Equal<string>(ConnectionStrings.AdventureWorksNamed, GetShiftTableData.ConnectionStringOrName)

    [<Fact>]
    member __.NullableDateTimeColumn() = 

        let table = new ProductCostHistory()
        use cmd = new SqlCommandProvider<"SELECT * FROM Production.ProductCostHistory WHERE EndDate IS NOT NULL", ConnectionStrings.AdventureWorksNamed, ResultType.DataReader>()
        cmd.Execute() |> table.Load
        
        Assert.NotEmpty(table.Rows)

        let row = table.Rows.[0]

        Assert.True(row.EndDate.IsSome)
        //dymanic accessor
        Assert.NotEqual(box DBNull.Value, row.["EndDate"])

        row.EndDate <- None

        Assert.True(row.EndDate.IsNone)

    [<Fact>]
    member __.SqlCommandTableInsert() = 
        use cmd = 
            new SqlCommandProvider<"SELECT Name, StartTime, EndTime FROM HumanResources.Shift", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>()
        let t = cmd.Execute()
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        let row = t.NewRow()
        row.Name <- "French coffee break"
        row.StartTime <- TimeSpan.FromHours 10.
        row.EndTime <- TimeSpan.FromHours 12.
        t.Rows.Add row
        let rowsInserted = t.Update(conn, tran)
        Assert.Equal(1, rowsInserted)


    [<Fact>]
    member __.SqlCommandTableUpdate() = 
        use cmd = 
            new SqlCommandProvider<"SELECT ShiftID, Name, StartTime, EndTime, ModifiedDate FROM HumanResources.Shift", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>()
        let t = cmd.Execute()
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        let row = t.Rows.[0]
        row.ModifiedDate <- DateTime.Now.Date
        let rowsAffected = t.Update(conn, tran)
        Assert.Equal(1, rowsAffected)
    
    [<Fact(Skip = "Don't execute for usual runs. Too slow.")>]
    member __.SqlCommandTable_RespectsTimeout() = 
        let tbl = new AdventureWorksDataTables.Production.Tables.Location()

        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
    
        tbl.AddRow("test", Some 12.12M, Some 23.23M, Some System.DateTime.UtcNow)
        let rowcount = tbl.Update(connection = conn, transaction = tran, timeout = TimeSpan.FromSeconds(5.0))

        let row = tbl.Rows |> Seq.head

        row.Name <- "Slow Trigger"

        let sw = new System.Diagnostics.Stopwatch()
        sw.Start()

        let mutable completed = false
        try
            let rowcount = tbl.Update(connection = conn, transaction = tran, timeout = TimeSpan.FromSeconds(5.0))
            completed <- true
        with
        | ex ->
            ()

        sw.Stop()

        if completed then
            failwith "Update should not have completed.  Operation should have taken 15 seconds and timeout was set to 5 seconds."
        else if sw.Elapsed.TotalSeconds > 6.0 then
            failwith "Timeout was set to 5 seconds.  Operation should have failed earlier than this"
        else if sw.Elapsed.TotalSeconds < 4.0 then
            failwith "Timeout was set to 5 seconds.  Operation should have lasted longer than this.  The test may be set up incorrectly"



    [<Fact>]
    member __.NewRowAndBulkCopyWithTrsansactionScope() = 
        try
            use tran = new TransactionScope()
            let t = new ShiftTable()
    
            //erased method to provide static typing
            let now = DateTime.Now.Date
            t.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some now)
            t.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some now)

            //check type. Should DateTime not option<DateTime>
            Assert.Equal<DateTime>(now, t.Rows.[0].ModifiedDate)

            use getRowsCount = new GetRowCount()
            let rowsBefore = getRowsCount.Execute().Value.Value
        
            //shortcut, convenience method
            t.BulkCopy()

            let rowsAdded = getRowsCount.Execute().Value.Value - rowsBefore
            Assert.Equal(t.Rows.Count, rowsAdded)
            
            tran.Complete()
        finally
            //compenstating tran
            let t2 = new ShiftTable()
            use getShiftTableData = new GetShiftTableData()
            getShiftTableData.Execute() |> t2.Load
            for r in t2.Rows do
                if r.Name = "French coffee break" || r.Name = "Spanish siesta"
                then 
                    r.Delete()
            let rowsAffected = t2.Update()
            assert (rowsAffected = 2)

    [<Fact>]
    member __.ColumnWithSpaceInNameAndDefaultValue() =
        use tran = new TransactionScope()
        let t = new AdventureWorks.dbo.Tables.TableHavingColumnNamesWithSpaces()
        t.AddRow()
        Assert.Equal(1, t.Update())

    [<Fact>]
    member __.``Can use Table type when ResultType = ResultType.DataTable`` () =
        let t : GetArbitraryDataAsDataTable.Table = (new GetArbitraryDataAsDataTable()).Execute()
        for (_: GetArbitraryDataAsDataTable.Table.Row) in t.Rows do
            ()

        Assert.NotNull(t)
        Assert.Equal(1, t.Rows.[0].a)
        Assert.Equal(2, t.Rows.[0].b)
        Assert.Equal(3, t.Rows.[0].c)
        Assert.Equal(None, t.Rows.[0].d)

    [<Fact>]
    member __.``Can use datacolumns and access like a normal DataRow`` () =
        let t = (new GetArbitraryDataAsDataTable()).Execute()
        let r = t.Rows.[0]
      
        Assert.Equal(1, r.[t.Columns.a] :?> int)
        Assert.Equal(2, r.[t.Columns.b] :?> int)
        Assert.Equal(3, r.[t.Columns.c] :?> int)
        // getting value same way as a plain datatable still yields DBNull
        Assert.Equal(DBNull.Value, r.[t.Columns.d] :?> DBNull)

    [<Fact>]
    member __.``Can use datacolumns GetValue and SetValue methods`` () =
        let t = (new GetArbitraryDataAsDataTable()).Execute()
        let r = t.Rows.[0]
        let a, b, c, d =
          t.Columns.a.GetValue(r)
          , t.Columns.b.GetValue(r)
          , t.Columns.c.GetValue(r)
          , t.Columns.d.GetValue(r)

        Assert.Equal(r.a, a)
        Assert.Equal(r.b, b)
        Assert.Equal(r.c, c)
        Assert.Equal(r.d, d)

        // need to make column readonly = false in order to use SetValue from the DataColumn
        t.Columns.a.ReadOnly <- false
        t.Columns.b.ReadOnly <- false
        t.Columns.c.ReadOnly <- false
        t.Columns.d.ReadOnly <- false

        t.Columns.a.SetValue(r, 108)
        t.Columns.b.SetValue(r, 108)
        t.Columns.c.SetValue(r, 108)
        t.Columns.d.SetValue(r, Some 108)

        Assert.Equal(r.a, 108)
        Assert.Equal(r.b, 108)
        Assert.Equal(r.c, 108)
        Assert.Equal(r.d, Some 108)

    [<Fact>]
    member __.``Can use DataColumnCollection`` () =

        let table = (new GetArbitraryDataAsDataTable()).Execute()

        let columnCollection : System.Data.DataColumnCollection =
            // this is more involved than just doing table.Columns because
            // DataColumnCollection is a sealed class, and the generative TP
            // attaches properties to a fake inherited type
            // In order to get a real DataColumnCollection, just use the table
            // as a normal DataTable.
            let table : System.Data.DataTable = table :> _
            table.Columns
        Assert.Equal(table.Columns.Count, columnCollection.Count)
    
    [<Fact>]
    member __.``Can use datacolumns on SqlProgrammabilityProvider`` () =
        let products = new AdventureWorks.Production.Tables.Product()
        
        let product = products.NewRow()
        
        // can use SetValue
        let newName = "foo"
        products.Columns.Name.SetValue(product, newName)
        Assert.True(product.Name = newName)

        // use as plain DataColumns
        let name = product.[products.Columns.Name] :?> string
        Assert.True(product.Name = name)

        // can use GetValue
        product.FinishedGoodsFlag <- true
        let finishedGoodsFlag = products.Columns.FinishedGoodsFlag.GetValue(product)
        Assert.Equal(product.FinishedGoodsFlag, finishedGoodsFlag)

    [<Fact>]
    member __.``Can use Table property on SqlCommandProvider's rows`` () =
        use cmd = new GetArbitraryDataAsDataTable()
        let t = cmd.Execute()
        Assert.Equal<GetArbitraryDataAsDataTable.Table>(t.Rows.[0].Table, t)

    [<Fact>]
    member __.``Can use Table property on SqlProgrammabilityProvider's rows`` () =
        let products = new AdventureWorks.Production.Tables.Product()
        
        let product = products.NewRow()
        product.Name <- "foo"
        
        // can access typed table from row
        let name = product.[product.Table.Columns.Name] :?> string
        Assert.True(product.Name = name)

    [<Fact>]
    member __.ContinueUpdateOnErrorFalse() =
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
        use cmd = 
            new SqlCommandProvider<"SELECT TOP 2 * FROM Purchasing.ShipMethod", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>(conn, tran)
        let table = cmd.Execute()
        table.Rows.[0].ShipRate <- -1M
        table.Rows.[1].ShipRate <- table.Rows.[1].ShipRate * 1.1M

        Assert.Throws<SqlException>(fun() -> table.Update() |> box) |> ignore

    [<Fact>]
    member __.ContinueUpdateOnErrorTrue() =
        use conn = new SqlConnection(connectionString = adventureWorks)
        conn.Open()
        use tran = conn.BeginTransaction()
        use cmd = 
            new SqlCommandProvider<"SELECT TOP 2 * FROM Purchasing.ShipMethod", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>(conn, tran)
        let table = cmd.Execute()
        table.Rows.[0].ShipRate <- -1M
        table.Rows.[1].ShipRate <- table.Rows.[1].ShipRate * 1.1M

        let rowsAffected = table.Update(continueUpdateOnError = true)

        Assert.Equal(1, rowsAffected)
    
    [<Fact>]
    member __.``can build arbitrary data table from inline sql``() =
        use table = new SqlCommandProvider<"SELECT 1 a, 2 b", ConnectionStrings.AdventureWorksNamed, ResultType.DataTable>.Table()
        let r = table.NewRow()
        table.Columns.a.set_ReadOnly false
        table.Columns.a.SetValue(r, 2)
        Assert.Equal(r.a , 2)
        
    [<Fact>]
    member __.``can build arbitrary table and columns exist``() =
        let t = new GetArbitraryDataAsDataTable.Table()
        let r = t.NewRow()
        t.Columns.a.set_ReadOnly false
        t.Columns.a.SetValue(r, 1)
        Assert.Equal(r.a , 1)
               
    [<Fact>]
    member __.``can't update on arbitrarilly constructed table``() =
        let t = new GetArbitraryDataAsDataTable.Table()
        let e = Assert.Throws(fun () -> t.Update() |> ignore)
        Assert.Equal<string>("This command wasn't constructed from SqlProgrammabilityProvider, call to Update is not supported.", e.Message)
       