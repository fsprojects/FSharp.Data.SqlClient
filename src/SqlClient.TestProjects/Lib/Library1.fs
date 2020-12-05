module DataAccess

open FSharp.Data.SqlClient

type Get42 = SqlCommandProvider<"SELECT 42", "name=AdventureWorks", SingleRow = true>

let get42() =
    use cmd = new Get42()
    cmd.Execute()

type AdventureWorks = SqlProgrammabilityProvider<"name=AdventureWorks">

let getShiftTable() = 
    let shifts = new AdventureWorks.HumanResources.Tables.Shift()
    use cmd = new SqlCommandProvider<"select * from HumanResources.Shift", "name=AdventureWorks", ResultType.DataReader>()
    shifts.Load <| cmd.Execute()
    shifts

    