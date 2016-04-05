namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient
open System.Collections.Generic
open FSharp.Data.SqlClient

[<Sealed>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type DataTable<'T when 'T :> DataRow> (knownSelectCommandOrDesignTimeInfo) = 
    inherit DataTable(match knownSelectCommandOrDesignTimeInfo with | Choice1Of2 (tableName, _) -> tableName | _ -> null)

    let tableName = base.TableName
    let rows = base.Rows
    let getSelectCommand maybeRuntimeConnection maybeRuntimeTransaction =
        let makeSelectCommand connection = 
            let selectCommand = new SqlCommand("SELECT * FROM " + tableName)
            selectCommand.Connection <- connection
            selectCommand

        match knownSelectCommandOrDesignTimeInfo with
        | Choice1Of2 (_, (getDesignTimeConnection:Lazy<_>)) ->
            match maybeRuntimeTransaction, maybeRuntimeConnection with
            | Some (tran:SqlTransaction), _ -> 
                let command = makeSelectCommand tran.Connection
                command.Transaction <- tran
                command
            | None, Some connection ->  
                makeSelectCommand connection 
            | _ -> makeSelectCommand (getDesignTimeConnection.Value)
        | Choice2Of2 knownSelectCommand -> knownSelectCommand

    member __.Rows : IList<'T> = {
        new IList<'T> with
            member __.GetEnumerator() = rows.GetEnumerator()
            member __.GetEnumerator() : IEnumerator<'T> = (Seq.cast<'T> rows).GetEnumerator() 

            member __.Count = rows.Count
            member __.IsReadOnly = rows.IsReadOnly
            member __.Item 
                with get index = downcast rows.[index]
                and set index row = 
                    rows.RemoveAt(index)
                    rows.InsertAt(row, index)

            member __.Add row = rows.Add row
            member __.Clear() = rows.Clear()
            member __.Contains row = rows.Contains row
            member __.CopyTo(dest, index) = rows.CopyTo(dest, index)
            member __.IndexOf row = rows.IndexOf row
            member __.Insert(index, row) = rows.InsertAt(row, index)
            member __.Remove row = rows.Remove(row); true
            member __.RemoveAt index = rows.RemoveAt(index)
    }

    member __.NewRow(): 'T = downcast base.NewRow()

    member this.Update(?connection, ?transaction, ?batchSize) = 
        
        let selectCommand = getSelectCommand connection transaction
        use dataAdapter = new SqlDataAdapter(selectCommand)
        use commandBuilder = new SqlCommandBuilder(dataAdapter) 
        use __ = dataAdapter.RowUpdating.Subscribe(fun args ->
            if  args.Errors = null && args.StatementType = StatementType.Insert
                && defaultArg batchSize dataAdapter.UpdateBatchSize = 1
            then 
                let columnsToRefresh = ResizeArray()
                for c in this.Columns do
                    if c.AutoIncrement  
                        || (c.AllowDBNull && args.Row.IsNull c.Ordinal)
                    then 
                        columnsToRefresh.Add( "inserted." + commandBuilder.QuoteIdentifier c.ColumnName)

                if columnsToRefresh.Count > 0
                then                        
                    let outputClause = columnsToRefresh |> String.concat "," |> sprintf " OUTPUT %s"
                    let cmd = args.Command
                    let sql = cmd.CommandText
                    let insertOutputClauseAt = 
                        match sql.IndexOf( " DEFAULT VALUES") with
                        | -1 -> sql.IndexOf( " VALUES")
                        | pos -> pos
                    cmd.CommandText <- sql.Insert(insertOutputClauseAt, outputClause)
                    cmd.UpdatedRowSource <- UpdateRowSource.FirstReturnedRecord
        )

        connection |> Option.iter dataAdapter.SelectCommand.set_Connection
        transaction |> Option.iter dataAdapter.SelectCommand.set_Transaction
        batchSize |> Option.iter dataAdapter.set_UpdateBatchSize

        dataAdapter.Update(this)

    member this.BulkCopy(?connection, ?copyOptions, ?transaction, ?batchSize, ?timeout: TimeSpan) = 
        
        let selectCommand = getSelectCommand connection transaction
        let connection = defaultArg connection selectCommand.Connection
        use __ = connection.UseLocally()
        use bulkCopy = 
            new SqlBulkCopy(
                connection, 
                copyOptions = defaultArg copyOptions SqlBulkCopyOptions.Default, 
                externalTransaction = defaultArg transaction selectCommand.Transaction
            )
        bulkCopy.DestinationTableName <- tableName
        batchSize |> Option.iter bulkCopy.set_BatchSize
        timeout |> Option.iter (fun x -> bulkCopy.BulkCopyTimeout <- int x.TotalSeconds)
        bulkCopy.WriteToServer this