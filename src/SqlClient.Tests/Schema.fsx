module Test.Schema

open System.Data
open System.Data.SqlClient

[<Literal>]
let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"

let conn = new SqlConnection(connectionString)
do conn.Open()

let ProcedureParameters() = 
    seq { 
                for r in conn.GetSchema("ProcedureParameters").Rows do
                if string r.["specific_catalog"] = conn.Database && string r.["specific_name"] = "Swap" then
                    yield r.["specific_name"], r.["parameter_name"], r.["data_type"], r.["specific_schema"], r.ItemArray

    }
    |> Seq.iter (printfn "%A")

let Procedures() = 
    let rows = conn.GetSchema("Procedures").Rows
    seq { 
                for r in rows do
                //if string r.["specific_catalog"] = conn.Database then
                    yield r.["specific_name"], r.["specific_schema"]

    }
    |> Seq.iter (printfn "%A")

let DataTypes() = 
    seq { 
                for row in conn.GetSchema("DataTypes").Rows do
                    yield string row.["TypeName"],  unbox<int> row.["ProviderDbType"], string row.["DataType"]
    }
    |> Seq.iter (printfn "%A")

