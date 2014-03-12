module Test.SMO

open System.Data
open System.Data.SqlClient
open Microsoft.SqlServer.Management.Smo
open Microsoft.SqlServer.Management.Common
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
let conn = new SqlConnection(connectionString)
do conn.Open()
let db()  = Server( ServerConnection(conn)).Databases.[conn.Database]

//[<Fact>]
let UDTs() =
    db().UserDefinedDataTypes
    |> Seq.cast<UserDefinedDataType>
    |> Seq.map(fun p -> p.ID, p.Name, p.SystemType)    
    |> Seq.iter (printfn "%A")

//[<Fact>]
let UDTFs() =
    db().UserDefinedFunctions
    |> Seq.cast<UserDefinedFunction> 
    |> Seq.where(fun p->not p.IsSystemObject)
    |> Seq.map(fun p -> p.Name, p.Columns, p.DataType)    
    |> Seq.iter (printfn "%A")

//[<Fact>]
let ``List stored procedures``() = 
    db().StoredProcedures 
    |> Seq.cast<StoredProcedure> 
    |> Seq.filter (fun c -> not c.IsSystemObject)
    |> Seq.iter (printfn "%A")

//[<Fact>]
let Parameters() = 
    db().StoredProcedures.["Swap","dbo"].Parameters
    |> Seq.cast<StoredProcedureParameter>     
    |> Seq.iter (fun p -> printfn "%A" (p.IsReadOnly, p.DataType, p.DefaultValue, p.Name))