#r "System.Data.DataSetExtensions"

open System
open System.Data
open System.Data.SqlClient

let connectionString = "Data Source=mitekm-pc2;Initial Catalog=AdventureWorks2012;Integrated Security=True"
let conn = new SqlConnection(connectionString)
conn.Open()

let dataTypes = conn.GetSchema("DataTypes")
let columnNames = [ for c in dataTypes.Columns -> c.ColumnName ]
let xs = 
    dataTypes.AsEnumerable() 
    |> Seq.map (fun x -> 
        [ 
            for c in ["TypeName" ; "ProviderDbType" ; "DataType"] -> 
                if c = "ProviderDbType" 
                then c, Enum.Parse(typeof<SqlDbType>,string x.[c])
                else c, x.[c] 
        ]
    )
    |> Seq.toArray 
    |> printfn "DataTypes: \n%A"

//let spParams = conn.GetSchema("Procedure Parameters").AsEnumerable();;

//Run sp
if conn.State <> ConnectionState.Open then conn.Open()
let sqlScript = "SELECT * FROM Production.Product"
let cmd = new SqlCommand(sqlScript, conn)
let table = new DataTable() 
table.Load <| cmd.ExecuteReader()
table.Rows.[0].["Name"] <- string table.Rows.[0].["Name"] + "1"
let sqlAdapter = new SqlDataAdapter(cmd)
printfn "Select command: %A" sqlAdapter.SelectCommand
printfn "Update command: %A" sqlAdapter.UpdateCommand
printfn "Updated recotds %i" <| sqlAdapter.Update table
conn.Close()

