namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Collections.Generic
open FSharp.Data.SqlClient.Internals

[<Sealed>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type DataTable<'T when 'T :> DataRow>(selectCommand: IDbCommand, ?connectionString: Lazy<string>) = 
    inherit DataTable()

    let rows = base.Rows

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

    member private this.IsDirectTable = this.TableName <> null
    
    member this.Update(?connection, ?transaction, ?batchSize, ?continueUpdateOnError, ?timeout: TimeSpan) = 
        // not supported on all DataTable instances
        let selectCommand =
          match selectCommand with
          | null -> failwith "This command wasn't constructed from SqlProgrammabilityProvider, call to Update is not supported."
          | :? SqlCommand as selectCommand -> selectCommand
          | _ -> failwithf "This command has type %s, this is only supported for commands instanciated with System.Data.SqlClient db types." (selectCommand.GetType().FullName)
        
        connection |> Option.iter selectCommand.set_Connection
        transaction |> Option.iter selectCommand.set_Transaction 
        
        if selectCommand.Connection = null && this.IsDirectTable 
        then 
            assert(connectionString.IsSome)
            selectCommand.Connection <- new SqlConnection( connectionString.Value.Value)

        use dataAdapter = new SqlDataAdapter(selectCommand)
        use commandBuilder = new SqlCommandBuilder(dataAdapter) 
        use __ = dataAdapter.RowUpdating.Subscribe(fun args ->
            timeout |> Option.iter (fun x -> args.Command.CommandTimeout <- int x.TotalSeconds)

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

        batchSize |> Option.iter dataAdapter.set_UpdateBatchSize
        continueUpdateOnError |> Option.iter dataAdapter.set_ContinueUpdateOnError

        dataAdapter.Update(this)

    member this.BulkCopy(?connection, ?copyOptions, ?transaction, ?batchSize, ?timeout: TimeSpan) = 
        
        let conn', tran' = 
            match connection, transaction with
            | _, Some(t: SqlTransaction) -> t.Connection, t
            | Some c, None -> c, null
            | None, None ->
                let selectCommand =
                  match selectCommand with
                  | null -> failwith "To issue BulkCopy on this table, you need to provide your own connection or transaction"
                  | :? SqlCommand as selectCommand -> selectCommand
                  | _ -> failwithf "This command has type %s, this is only supported for commands instanciated with System.Data.SqlClient db types." (selectCommand.GetType().FullName)

                if this.IsDirectTable
                then 
                    assert(connectionString.IsSome)
                    new SqlConnection(connectionString.Value.Value), null
                else
                    selectCommand.Connection, selectCommand.Transaction

        use __ = conn'.UseLocally()
        let options = defaultArg copyOptions SqlBulkCopyOptions.Default
        use bulkCopy = new SqlBulkCopy(conn', options, tran')
        bulkCopy.DestinationTableName <- this.TableName
        batchSize |> Option.iter bulkCopy.set_BatchSize
        timeout |> Option.iter (fun x -> bulkCopy.BulkCopyTimeout <- int x.TotalSeconds)
        bulkCopy.WriteToServer this

#if WITH_LEGACY_NAMESPACE
namespace FSharp.Data
open System
open System.Data
[<Obsolete("use 'FSharp.Data.SqlClient.DataTable' instead");AutoOpen>]
type DataTable<'T when 'T :> DataRow> = FSharp.Data.SqlClient.DataTable<'T>
#endif