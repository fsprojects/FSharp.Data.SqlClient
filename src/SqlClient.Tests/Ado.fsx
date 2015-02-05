#load "ConnectionStrings.fs"
open System
open System.Data
open System.Data.SqlClient

let getComponents (productId ,checkDate) = 
    use connection = new SqlConnection(ConnectionStrings.AdventureWorks)

    let sqlCommand = new SqlCommand("dbo.uspGetWhereUsedProductID", connection)
    sqlCommand.CommandType <- CommandType.StoredProcedure

    sqlCommand.Parameters.AddWithValue("@StartProductID", productId) |> ignore
    sqlCommand.Parameters.AddWithValue("@CheckDate", checkDate) |> ignore
    connection.Open()

    let reader = sqlCommand.ExecuteReader()
    [
        while reader.Read() do
            let productAssemblyId = reader.["ProductAssemblyId"] :?> int
            let componentId = reader.["ComponentId"] :?> int
            let description = string reader.["ComponentDesc"]
            let qty = reader.["TotalQuantity"] :?> decimal
            yield productAssemblyId, componentId, description, qty
    ]

getComponents (1, DateTime(2013,1,1))