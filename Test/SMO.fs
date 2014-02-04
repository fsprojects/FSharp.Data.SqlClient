module Test.SMO

open System.Data
open System.Data.SqlClient
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
let conn = new SqlConnection(connectionString)
let server = Server( ServerConnection(conn))
let db = server.Databases.[conn.Database]
let sps = db.StoredProcedures 


[<Fact>]
let ``List stored procedures``() = 
    sps
    |> Seq.cast<StoredProcedure> 
    |> Seq.filter (fun c -> not c.IsSystemObject)
    |> Seq.iter (printfn "%A")

[<Fact>]
let Parameters() = 
    sps.["uspGetBillOfMaterials"].Parameters
    |> Seq.cast<StoredProcedureParameter>     
    |> Seq.iter (printfn "%A")

[<Fact>]
let Execute() = 
    let sp = sps.["uspPrintError"]
    let command = new SqlCommand(sp.Name, new SqlConnection(connectionString), CommandType = CommandType.StoredProcedure)
    command.Connection.Open()
    let reader = command.ExecuteReader(CommandBehavior.CloseConnection)
    while reader.Read() do
        printfn "%A" reader.FieldCount

