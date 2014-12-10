
#r "System.Transactions"

open System
open System.IO
open System.Data
open System.Data.SqlClient
open System.Data.SqlTypes

let conn = new SqlConnection(@"Data Source=.;Initial Catalog=ThermionDB;Integrated Security=True")
conn.Open()

//let reader = cmd.ExecuteReader()
//let reader = cmd.ExecuteReader()
//let schema = conn.GetSchema("DataTypes")
//[for c in schema.Columns -> c.ColumnName ]
//[for r in schema.Rows -> [for c in schema.Columns do if not(r.IsNull(c)) then yield sprintf "%s - %A" c.ColumnName r.[c]] |> String.concat "\n" |> sprintf "%s\n\n" ]
//[for r in schema.Rows -> r.["TypeName"], r.["ProviderDbType"], r.["DataType"].GetType().Name]
//
//let metaSchema = conn.GetSchema("MetaDataCollections")
//[for c in metaSchema.Columns -> c.ColumnName ]
//[for r in metaSchema.Rows -> [for c in metaSchema.Columns do if not(r.IsNull(c)) then yield sprintf "%s - %A" c.ColumnName r.[c]] |> String.concat "\n" |> sprintf "%s\n\n" ]


let cmd = new SqlCommand("SELECT * FROM Thermion.TimeSeries", conn)
let adapter = new SqlDataAdapter(cmd)
let dataTable = adapter.FillSchema(new DataTable(), SchemaType.Source)
dataTable.Columns.["tsGuid"].AllowDBNull <- true
//dataTable.Columns.["createdTmsp"].ExtendedProperties.["Default"] <- "hehe"
let serializedSchema = 
    use writer = new StringWriter()
    dataTable.WriteXmlSchema writer
    writer.ToString()

let dataTableClone = new DataTable()
dataTableClone.ReadXmlSchema(new StringReader(serializedSchema))
dataTable.Columns.["tsGuid"].AllowDBNull

//dataTable.Columns.Remove("createdTmsp")

//dataTable.Columns.Remove("createdTmsp")
//dataTable.Columns.["createdTmsp"].AllowDBNull <- true
//dataTable.Columns.["createdTmsp"].DefaultValue <- null //DBNull.Value
let row = dataTable.NewRow()
row.["description"] <- "my_desc9"
row.["createdTmsp"] <- DateTime.Now.AddDays(10.)
//dataTable.LoadDataRow([| null; box "my_desc3" |], LoadOption.Upsert)

let adapter2 = new SqlDataAdapter("SELECT importBatch, description FROM ImportBatch", conn)
let builder = new SqlCommandBuilder(adapter2)
adapter.InsertCommand <- builder.GetInsertCommand()
dataTable.Rows.Add row
adapter.Update dataTable

let sqlBulkCopy = new SqlBulkCopy(conn)
sqlBulkCopy.DestinationTableName <- "dbo.ImportBatch"
sqlBulkCopy.ColumnMappings.Add("importBatch", "importBatch")
sqlBulkCopy.ColumnMappings.Add("description", "description")
sqlBulkCopy.WriteToServer([| row |])

dataTable.Columns.[0]

let schemaStorage = new StringWriter()
dataTable.WriteXmlSchema(schemaStorage)
let clone = new DataTable()
clone.Columns.Count
clone.ReadXmlSchema(new StringReader(schemaStorage.ToString()))


