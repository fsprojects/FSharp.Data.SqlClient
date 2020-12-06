module FSharp.Data.Application

//open WebApi
//let testExternalQuery = new DataAccess.QueryProducts()

open System
open System.Windows
open System.Windows.Controls
open System.Data.SqlClient

open FSharp.Data.SqlClient

[<Literal>]
let queryTableSql = "select top 5 AddressID, AddressLine1, City, SpatialLocation from Person.Address where AddressLine1 like @startsWith"

type Query = SqlCommandProvider<queryTableSql, "name=AdventureWorks", ResultType=ResultType.DataTable>

[<STAThread>]
[<EntryPoint>]
let main argv = 
    let mainWindow : Window = Uri("/Mainwindow.xaml", UriKind.Relative) |> Application.LoadComponent |> unbox
    let close : Button = mainWindow.FindName "Close" |> unbox
    let save : Button = mainWindow.FindName "Save" |> unbox
    let grid : DataGrid = mainWindow.FindName "Grid" |> unbox

    let cmd = new Query()
    let data = cmd.Execute(startsWith = "c%")
    grid.ItemsSource <- data.DefaultView

    close.Click.Add <| fun _ -> mainWindow.Close()
    save.Click.Add <| fun _ -> data.Update() |> ignore

    Application().Run mainWindow
