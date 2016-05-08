module FSharp.Data.SynonymsTests

open System
open System.Data
open System.Data.SqlClient
open ProgrammabilityTest
open Xunit

type AdventureWorks = ProgrammabilityTest.AdventureWorks

[<Fact>]
let TVFSynonym() = 
    let personId = 42
    let actual = 
        use cmd = new AdventureWorks.HumanResources.GetContactInformation()
        cmd.ExecuteSingle(personId)
        |> Option.map(fun x -> x.FirstName, x.LastName)

    let expected = 
        use cmd = new ProgrammabilityTest.GetContactInformation()
        cmd.ExecuteSingle(personId)
        |> Option.map(fun x -> x.FirstName, x.LastName)

    Assert.Equal(expected, actual)

[<Fact>]
let SPSynonym() = 
    let personId = 42
    let actual = 
        use cmd = new AdventureWorks.HumanResources.GetEmployeeManagers()
        [ for x in cmd.Execute(personId) -> sprintf "%A.%A" x.FirstName x.LastName ]

    let expected = 
        use cmd = new AdventureWorks.dbo.uspGetEmployeeManagers()
        [ for x in cmd.Execute(personId) -> sprintf "%A.%A" x.FirstName x.LastName ]

    Assert.Equal<_ list>(expected, actual)


[<Fact>]
let TableSynonym() = 
    let adventureWorks = FSharp.Configuration.AppSettings<"app.config">.ConnectionStrings.AdventureWorks
    use conn = new SqlConnection(connectionString = adventureWorks)
    conn.Open()
    use tran = conn.BeginTransaction()

    use cmd = new GetRowCount(transaction = tran) 
    Assert.Equal(Some( Some 3), cmd.Execute())
    
    let t = new AdventureWorks.dbo.Tables.HRShift()
    let row = t.NewRow()
    row.Name <- "French coffee break"
    row.StartTime <- TimeSpan.FromHours 10.
    row.EndTime <- TimeSpan.FromHours 12.
    t.Rows.Add row
    let rowsInserted = t.Update(conn, tran)
    Assert.Equal(1, rowsInserted)

    Assert.Equal(Some( Some 4), cmd.Execute())

