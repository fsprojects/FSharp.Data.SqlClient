module FSharp.Data.SqlClient.Application

open System
open System.Windows
open System.Windows.Controls

open FSharp.Data.SqlClient

[<Literal>]
//let queryTableSql = "SELECT * FROM Production.Product WHERE SellStartDate > @SellStartDate"
let queryTableSql = "SELECT * FROM Production.Product"

type Query = SqlCommand<queryTableSql, ConnectionStringName="AdventureWorks2012", ResultType=ResultType.DataTable>

[<STAThread>]
[<EntryPoint>]
let main argv = 
    let mainWindow : Window = Uri("/Mainwindow.xaml", UriKind.Relative) |> Application.LoadComponent |> unbox
    let close : Button = mainWindow.FindName "Close" |> unbox
    let grid : DataGrid = mainWindow.FindName "Grid" |> unbox

    let cmd = Query()
    //cmd.SellStartDate <- DateTime.Parse "7/1/2006"
    let data = cmd.AsyncExecute() |> Async.RunSynchronously
    grid.ItemsSource <- data

    close.Click.Add <| fun _ -> mainWindow.Close()

    Application().Run mainWindow
