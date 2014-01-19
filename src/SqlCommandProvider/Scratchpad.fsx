
//#r "../bin/Debug/SqlCommandProvider.dll"
#r "System.Data.DataSetExtensions"

open System
open System.Data
open System.Data.SqlClient
//open FSharp.Data.SqlClient.Extensions
let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()

let sqlScript = "
    declare @yCopy as int = @y\n
    SELECT @x + CASE WHEN @yCopy IS NULL THEN 1 ELSE @yCopy END\n
"
let cmd = new SqlCommand(sqlScript, conn)
cmd.Parameters.AddWithValue("@x", box 11) |> ignore
cmd.Parameters.AddWithValue("@y", DBNull.Value) |> ignore
let result = cmd.ExecuteScalar()


//for p in cmd.Parameters do printfn "Param: %s, type: %s, sqldbtype: %A, direction %A, IsNullable %b, Value: %A" p.ParameterName p.TypeName p.SqlDbType p.Direction p.IsNullable p.Value
open System.Runtime.ExceptionServices
let raise exn : 'a = ExceptionDispatchInfo.Capture(exn).Throw(); Unchecked.defaultof<'a>