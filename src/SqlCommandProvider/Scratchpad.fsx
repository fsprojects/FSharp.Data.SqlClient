
//#r "../bin/Debug/SqlCommandProvider.dll"
#r "System.Data.DataSetExtensions"

open System.Data
open System.Data.SqlClient
//open FSharp.Data.SqlClient.Extensions
let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()
//printfn "%A" <| conn.GetDataTypesMapping()

//let cmd = new SqlCommand("uspSearchCandidateResumes", conn, CommandType = CommandType.StoredProcedure)
let cmd = new SqlCommand("SELECT CAST(10 AS TINYINT) AS Value", conn)
SqlCommandBuilder.DeriveParameters cmd
let reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)


for p in cmd.Parameters do printfn "Param: %s, type: %s, sqldbtype: %A, direction %A, IsNullable %b, Value: %A" p.ParameterName p.TypeName p.SqlDbType p.Direction p.IsNullable p.Value

conn.GetSchema("DataTypes").AsEnumerable() 
|> Seq.map (fun r -> r.["TypeName"], r.["ProviderDbType"], r.["DataType"], r.["IsBestMatch"])
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


open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.RuntimeHelpers

let f = <@@ fun x y z -> x + y + z @@>
let args = [ [Expr.Value 2]; [Expr.Value 5]; [Expr.Value 20] ]
let value = Expr.Applications(f, args)
LeafExpressionConverter.EvaluateQuotation value