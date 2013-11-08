
#r "../bin/Debug/SqlCommandTypeProvider.dll"
#r "System.Data.DataSetExtensions"

open System.Data
open System.Data.SqlClient
open FSharp.Data.SqlClient.Extensions
let conn = new SqlConnection("Data Source=.;Initial Catalog=AdventureWorks2012;Integrated Security=True")
conn.Open()
printfn "%A" <| conn.GetDataTypesMapping()

conn.GetSchema("DataTypes").AsEnumerable() 
|> Seq.map (fun r -> r.Field("TypeName") |> string, r.Field("ProviderDbType") |> int, r.Field("DataType") |> string)
|> Array.ofSeq 

let cmd = new SqlCommand("SELECT CAST(10 AS TINYINT) AS Value", conn)
conn.Open()
let xs = cmd.ExecuteReader(CommandBehavior.CloseConnection ||| CommandBehavior.SingleRow)
let values = Array.zeroCreate<obj> xs.FieldCount
xs.Read()
let c = xs.GetValues values 
assert(c = values.Length)
printfn "values: %A" values
printfn "type: %s" <| values.[0].GetType().Name
printfn "value: %i" <| (unbox<sbyte> values.[0])

let cmdClone = cmd.Clone()
cmd.CommandText = cmdClone.CommandText
cmdClone.CommandText <- "SELECT 0 AS X"
//cmdClone.Connection <- cmd.Connection.Clone()
cmd.Connection = cmdClone.Connection