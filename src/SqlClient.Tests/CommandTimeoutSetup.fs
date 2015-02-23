module CommandTimeoutSetup

open System.Data.SqlClient
open Xunit
open FSharp.Data.CreateCommandFunctorTests
open FSharp.Data
open System

[<Literal>]
let connection = ConnectionStrings.AdventureWorksLiteral

 // just make sure we don't assert on the default value in the code whatever it is
let customTimeout = (new Random()).Next(512, 1024)

let prepareCommandWithConnectionContext =
  use conn = new SqlConnection(connection)
  conn.Open()
  let createCommand() =
    let c = conn.CreateCommand()
    c.CommandTimeout <- customTimeout
    c
  new GetDate(createCommand)

[<Fact>]
let ``Setting the command timeout isn't overridden when giving connection context``() =
    let getDate = prepareCommandWithConnectionContext
    let sqlCommand = (getDate :> ISqlCommand).Raw
    Assert.True(sqlCommand.CommandTimeout = customTimeout)

[<Fact>]
let ``Setting the command timeout is reflected in cloned command when giving connection context``() =
    let getDate = prepareCommandWithConnectionContext
    let sqlCommand = getDate.AsSqlCommand()
    Assert.True(sqlCommand.CommandTimeout = customTimeout)

[<Fact>]
let ``Setting the command timeout is reflected when giving connection string``() =
    let getDate = new GetDate(connection, customTimeout)
    let sqlCommand = getDate.AsSqlCommand()
    Assert.True(sqlCommand.CommandTimeout = customTimeout)
    let sqlCommand = (getDate :> ISqlCommand).Raw
    Assert.True(sqlCommand.CommandTimeout = customTimeout)

[<Fact>]
let ``Setting the command timeout is reflected when giving a transaction``() =
    use cx = new SqlConnection(connection)
    cx.Open()
    use tx = cx.BeginTransaction()
    let getDate = new GetDate(tx, customTimeout)
    let sqlCommand = getDate.AsSqlCommand()
    Assert.True(sqlCommand.CommandTimeout = customTimeout)
    let sqlCommand = (getDate :> ISqlCommand).Raw
    Assert.True(sqlCommand.CommandTimeout = customTimeout)

// please add more tests here if new constructors are added to provided SqlCommand