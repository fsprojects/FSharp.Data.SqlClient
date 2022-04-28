#r @"..\..\..\bin\netstandard2.0\FSharp.Data.SqlClient.dll"
#r @"bin\Debug\Lib.dll"
#r @"System.Configuration"
#r @"System.Transactions"

DataAccess.get42()

open System

//let shifts = new DataAccess.AdventureWorks.HumanResources.Tables.Shift()
let shifts = DataAccess.getShiftTable()
do 
    use tran = new System.Transactions.TransactionScope()
    shifts.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12.)
    shifts.Update() |> printfn "Records affected %i"
