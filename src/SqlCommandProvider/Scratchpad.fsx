
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

let parseConnParam (connectionStringOrName: string) =
    match connectionStringOrName.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
    | [| "" |] -> invalidArg "ConnectionStringOrName" "Value is empty!"
    | [| prefix; tail |] when prefix.ToLower() = "name" -> tail, true
    | _ -> connectionStringOrName, false

