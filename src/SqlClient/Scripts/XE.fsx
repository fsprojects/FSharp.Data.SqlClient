#I @"C:\Program Files (x86)\Microsoft SQL Server\120\Tools\Binn\ManagementStudio\Extensions\Application"

#r "System.Transactions"
#r "Microsoft.SqlServer.XE.Core.dll"
#r "Microsoft.SqlServer.XEvent.Linq.dll"

open Microsoft.SqlServer.XEvent.Linq
open System.Data.SqlClient

let connection = "Data Source=.;Initial Catalog=;Integrated Security=True"

let targetDatabase = "AdventureWorks2014"
let xeSession = "XE_Alter"
do 
    use conn = new SqlConnection(connection)
    conn.Open()
    conn.ChangeDatabase(targetDatabase)
    let createSession =
        sprintf "
            IF EXISTS(SELECT * FROM sys.server_event_sessions WHERE name='%s')
                DROP EVENT session %s ON SERVER

            CREATE EVENT SESSION [%s] 
            ON SERVER
                ADD EVENT sqlserver.object_altered
                (
                    ACTION (sqlserver.database_name)
                    WHERE  (sqlserver.database_name = '%s')
                )
                WITH (EVENT_RETENTION_MODE = NO_EVENT_LOSS, MAX_DISPATCH_LATENCY = 1 SECONDS)

            ALTER EVENT SESSION [XE_Alter] ON SERVER STATE = START
        " xeSession xeSession xeSession targetDatabase
    use cmd = new System.Data.SqlClient.SqlCommand(createSession, conn)
    cmd.ExecuteNonQuery() |> ignore

let (?) (event: PublishedEvent) (name: string) = unbox event.Fields.[name].Value 

do 
    use events = new QueryableXEventData(connection, xeSession, EventStreamSourceOptions.EventStream, EventStreamCacheOptions.DoNotCache)

    events
    |> Seq.filter(fun x -> string x.Fields.["ddl_phase"].Value = "Commit")
    |> Seq.iter (fun x ->
        let fs = x.Fields
        printfn "\nEvent %s.\nDDL Phase: %O.\nObject: id-%O; name-%O.\nDatabase:  id-%O; name-%O." x.Name fs.["ddl_phase"].Value fs.["object_id"].Value fs.["object_name"].Value fs.["database_id"].Value fs.["database_name"].Value 
    )
