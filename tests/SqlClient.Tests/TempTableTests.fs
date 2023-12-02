module FSharp.Data.SqlClient.Tests.TempTableTests
open FSharp.Data.SqlClient
open FSharp.Data.SqlClient.Tests

open FSharp.Data
open Xunit
open System.Data.SqlClient

type TempTable =
    SqlCommandProvider<
        TempTableDefinitions = "
            CREATE TABLE #Temp (
                Id INT NOT NULL,
                Name NVARCHAR(100) NULL)",
        CommandText = "
            SELECT Id, Name FROM #Temp",
        ConnectionStringOrName =
            ConnectionStrings.AdventureWorksLiteral>

[<Fact>]
let usingTempTable() =
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    conn.Open()

    use cmd = new TempTable(conn)

    cmd.LoadTempTables(
        Temp =
            [ TempTable.Temp(Id = 1, Name = Some "monkey")
              TempTable.Temp(Id = 2, Name = Some "donkey") ])

    let actual =
        cmd.Execute()
        |> Seq.map(fun x -> x.Id, x.Name)
        |> Seq.toList

    let expected = [
        1, Some "monkey"
        2, Some "donkey"
    ]

    Assert.Equal<_ list>(expected, actual)

[<Fact>]
let queryWithHash() =
    // We shouldn't mangle the statement when it's run
    use cmd =
        new SqlCommandProvider<
            CommandText = "
                SELECT Id, Name
                FROM
                (
                    SELECT 1 AS Id, '#name' AS Name UNION
                    SELECT 2, 'some other value'
                ) AS a
                WHERE Name = '#name'",
            ConnectionStringOrName =
                ConnectionStrings.AdventureWorksLiteral>(ConnectionStrings.AdventureWorksLiteral)

    let actual =
        cmd.Execute()
        |> Seq.map(fun x -> x.Id, x.Name)
        |> Seq.toList

    let expected = [
        1, "#name"
    ]

    Assert.Equal<_ list>(expected, actual)

type TempTableHash =
    SqlCommandProvider<
        TempTableDefinitions = "
            CREATE TABLE #Temp (
                Id INT NOT NULL)",
        CommandText = "
            SELECT a.Id, a.Name
            FROM
            (
                SELECT 1 AS Id, '#Temp' AS Name UNION
                SELECT 2, 'some other value'
            ) AS a
            INNER JOIN #Temp t ON t.Id = a.Id",
        ConnectionStringOrName =
            ConnectionStrings.AdventureWorksLiteral>

[<Fact>]
let queryWithHashAndTempTable() =
    // We shouldn't mangle the statement when it's run
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    conn.Open()

    use cmd = new TempTableHash(conn)

    cmd.LoadTempTables(
        Temp =
            [ TempTableHash.Temp(Id = 1) ])

    let actual =
        cmd.Execute()
        |> Seq.map(fun x -> x.Id, x.Name)
        |> Seq.toList

    let expected = [
        1, "#Temp"
    ]

    Assert.Equal<_ list>(expected, actual)