
open System
open System.Data
open System.Data.SqlClient
let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()

let cmd = new SqlCommand("select 'HAHA' as name, 1 as val where 1 = 1", conn)
let reader = cmd.ExecuteReader(CommandBehavior.CloseConnection ||| CommandBehavior.SingleRow)
let xs = seq {
    use closeReader = reader
    while reader.Read() do
        yield [
            for i = 0 to reader.FieldCount - 1 do
                if not(reader.IsDBNull(i)) 
                then yield reader.GetName(i), reader.GetValue(i)
        ] |> Map.ofList 
}

reader |> Seq.cast<IDataRecord> |> Seq.map( fun x -> Map.ofList [ for i = 0 to x.FieldCount - 1 do yield x.GetName(i), x.GetValue(i) ])
conn.State

let myFucc() = 
    use __ = { new System.IDisposable with member __.Dispose() = printfn "Bye-bye!" }
    ()
myFucc()

#r "Microsoft.SqlServer.ConnectionInfo"
#r "Microsoft.SqlServer.Management.Sdk.Sfc" 
#r "Microsoft.SqlServer.Smo"

open Microsoft.SqlServer.Management.Smo

let server = new Server(@"(LocalDb)\v11.0")
let db = server.Databases.["AdventureWorks2012"]
let sp = db.StoredProcedures.["uspSearchCandidateResumes"]
for p in sp.Parameters do printfn "Name: %s. defaulf value: %s" p.Name p.DefaultValue
sp.Parameters.[3].DefaultValue

