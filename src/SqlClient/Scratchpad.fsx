
open System
open System.Data
open System.Data.SqlClient
let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()

let cmd = new SqlCommand("select 1 where 1 = 1", conn)
let xs = [ 
    let reader = cmd.ExecuteReader(CommandBehavior.CloseConnection ||| CommandBehavior.SingleRow)
    while reader.Read() do yield reader.GetInt32(0)
]

#r "Microsoft.SqlServer.ConnectionInfo"
#r "Microsoft.SqlServer.Management.Sdk.Sfc" 
#r "Microsoft.SqlServer.Smo"

open Microsoft.SqlServer.Management.Smo

let server = new Server(@"(LocalDb)\v11.0")
let db = server.Databases.["AdventureWorks2012"]
let sp = db.StoredProcedures.["uspSearchCandidateResumes"]
for p in sp.Parameters do printfn "Name: %s. defaulf value: %s" p.Name p.DefaultValue
sp.Parameters.[3].DefaultValue

