module FSharp.Data.Experimental.Application

open System
open System.Windows
open System.Windows.Controls
open System.Data.SqlClient

open FSharp.Data.Experimental

[<Literal>]
let queryTableSql = "select top 5 AddressLine1, City, SpatialLocation from Person.Address where AddressLine1 like @startsWith"

type Query = SqlCommand<queryTableSql, ConnectionStringOrName="name=AdventureWorks2012", ResultType=ResultType.DataTable>

[<STAThread>]
[<EntryPoint>]
let main argv = 
    let mainWindow : Window = Uri("/Mainwindow.xaml", UriKind.Relative) |> Application.LoadComponent |> unbox
    let close : Button = mainWindow.FindName "Close" |> unbox
    let save : Button = mainWindow.FindName "Save" |> unbox
    let grid : DataGrid = mainWindow.FindName "Grid" |> unbox

    let cmd = Query()
    let data = cmd.Execute(startsWith = "c%")
    grid.ItemsSource <- data

    close.Click.Add <| fun _ -> mainWindow.Close()
    save.Click.Add <| fun _ -> 
        let sqlCommand = cmd.AsSqlCommand()
        let adapter = new SqlDataAdapter(sqlCommand)
        let builder = new SqlCommandBuilder(adapter)
        adapter.UpdateCommand <- builder.GetUpdateCommand()
        sqlCommand.Connection.Open()
        try
            adapter.Update data |> ignore
        finally
            sqlCommand.Connection.Close()

    Application().Run mainWindow
