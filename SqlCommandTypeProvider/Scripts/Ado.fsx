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
let cmd = new SqlCommand("HumanResources.uspUpdateEmployeePersonalInfo", conn, CommandType = CommandType.StoredProcedure)
SqlCommandBuilder.DeriveParameters cmd
let ps = cmd.Parameters

//ps.AddWithValue("@BusinessEntityID", 2)
//ps.AddWithValue("@NationalIDNumber", "245797967")
//ps.AddWithValue("@BirthDate", System.DateTime(1965, 09, 01))
//ps.AddWithValue("@MaritalStatus", "S")
//ps.AddWithValue("@Gender", "F")

ps.["@BusinessEntityID"].Value <- 2
ps.["@NationalIDNumber"].Value <- "245797967"
ps.["@BirthDate"].Value <- System.DateTime(1965, 09, 01)
ps.["@MaritalStatus"].Value <- "S"
ps.["@Gender"].Value <- "F"

let recordAffected = cmd.ExecuteNonQuery()
if conn.State = ConnectionState.Open then conn.Open()
let table = new DataTable() in table.Load <| cmd.ExecuteReader()
conn.Close()

