//#I @"C:\Program Files (x86)\Microsoft SQL Server\120\Tools\Binn\ManagementStudio\Extensions\Application"
#I @"C:\Program Files\Microsoft SQL Server\120\Shared"

#r "System.Transactions"
#r "Microsoft.SqlServer.XE.Core.dll"
#r "Microsoft.SqlServer.XEvent.Linq.dll"

open Microsoft.SqlServer.XEvent.Linq
open Microsoft.Data.SqlClient

let connection = "Data Source=.;Initial Catalog=master;Integrated Security=True"

let targetDatabase = "AdventureWorks2014"
let xeSession = "XE_Alter"

do 
    use conn = new SqlConnection(connection)
    conn.Open()
    conn.ChangeDatabase(targetDatabase)
    let createSession =
        sprintf "
            IF NOT EXISTS(SELECT * FROM sys.server_event_sessions WHERE name='%s')
            BEGIN
                CREATE EVENT SESSION [%s] 
                ON SERVER
                    ADD EVENT sqlserver.object_created
                    (
                        ACTION (sqlserver.database_name, sqlserver.sql_text)
                        WHERE  (sqlserver.database_name = '%s')
                    ),
                    ADD EVENT sqlserver.object_deleted
                    (
                        ACTION (sqlserver.database_name, sqlserver.sql_text)
                        WHERE  (sqlserver.database_name = '%s')
                    ),
		            ADD EVENT sqlserver.object_altered
                    (
                        ACTION (sqlserver.database_name, sqlserver.sql_text)
                        WHERE  (sqlserver.database_name = '%s')
                    ),

                    --trash events to make buffer to flush
                    ADD EVENT sqlos.async_io_completed,
                    ADD EVENT sqlserver.sql_batch_completed,
                    ADD EVENT sqlserver.sql_batch_starting,
                    ADD EVENT sqlserver.sql_statement_completed,
                    ADD EVENT sqlserver.sql_statement_recompile,
                    ADD EVENT sqlserver.sql_statement_starting,
                    ADD EVENT sqlserver.sql_transaction,
                    ADD EVENT sqlserver.sql_transaction_commit_single_phase
            END

            IF NOT EXISTS(SELECT * FROM sys.dm_xe_sessions WHERE name='%s')
            BEGIN
                ALTER EVENT SESSION [%s] ON SERVER STATE = START
            END
        " xeSession xeSession targetDatabase targetDatabase targetDatabase xeSession xeSession
    use cmd = new Microsoft.Data.SqlClient.SqlCommand(createSession, conn)
    cmd.ExecuteNonQuery() |> ignore

do 
    use events = new QueryableXEventData(connection, xeSession, EventStreamSourceOptions.EventStream, EventStreamCacheOptions.DoNotCache)

    for x in events do
        //printfn "Event name: %s" x.Name
        if x.Name = "object_altered" || x.Name = "object_created" || x.Name = "object_deleted"
        then
            let contains, ddl_phase = x.Fields.TryGetValue("ddl_phase") 
            if contains && string ddl_phase.Value = "Commit" 
            then 
                printfn "ddl_phase type: %A" ddl_phase.Type
        
                let fs = x.Fields
                let ac = x.Actions

    //            fs |> Seq.cast<PublishedEventField> |> Seq.map (fun e -> e.Name) |> String.concat ";" |> printfn "Fields: %s"
    //            x.Actions |> Seq.cast<PublishedAction> |> Seq.map (fun e -> e.Name) |> String.concat ";" |> printfn "Actions: %s"

                printfn "\nEvent %s.\nDDL Phase: %O.\nObject: id-%O; name-%O.\nDatabase:  id-%O; name-%O.\nSql text: %A" x.Name fs.["ddl_phase"].Value fs.["object_id"].Value fs.["object_name"].Value fs.["database_id"].Value ac.["database_name"].Value ac.["sql_text"].Value
                //printfn "\nEvent %s.\nDDL Phase: %O.\nObject: id-%O; name-%O.\nDatabase:  id-%O; name-%O." x.Name fs.["ddl_phase"].Value fs.["object_id"].Value fs.["object_name"].Value fs.["database_id"].Value fs.["database_name"].Value 
