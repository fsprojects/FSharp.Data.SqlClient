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
    |> Seq.iter (fun p -> printfn "%A %A" p.Name p.SystemType )

//[<Fact>]
let UDTTs() =
    db().UserDefinedTableTypes
    |> Seq.cast<UserDefinedTableType>     
    |> Seq.iter (fun p -> printfn "%A %A" p.Name p.Columns )

//[<Fact>]
let ``List stored procedures``() = 
    db().StoredProcedures 
    |> Seq.cast<StoredProcedure> 
    |> Seq.filter (fun c -> not c.IsSystemObject)
    |> Seq.iter (printfn "%A")

//[<Fact>]
let Parameters() = 
    db().StoredProcedures.["MyProc"].Parameters
    |> Seq.cast<StoredProcedureParameter>     
    |> Seq.iter (fun p -> printfn "%A %A" p.Name p.DataType )

[<Fact>]
let ProcedureParameters() = 
    seq { 
                for r in conn.GetSchema("ProcedureParameters").Rows do
                if string r.["specific_catalog"] = conn.Database && string r.["specific_name"] = "uspUpdateEmployeeLogin" then
                    yield r.["specific_name"], r.["parameter_name"], r.["data_type"], r.["specific_schema"]

    }
    |> Seq.iter (printfn "%A")

[<Fact>]
let Procedures() = 
    seq { 
                for r in conn.GetSchema("Procedures").Rows do
                if string r.["specific_catalog"] = conn.Database then
                    yield r.["specific_name"], r.["specific_schema"]

    }
    |> Seq.iter (printfn "%A")

[<Fact>]
let DataTypes() = 
    seq { 
                for row in conn.GetSchema("DataTypes").Rows do
                    yield string row.["TypeName"],  unbox<int> row.["ProviderDbType"], string row.["DataType"], row.ItemArray
    }
    |> Seq.iter (printfn "%A")
