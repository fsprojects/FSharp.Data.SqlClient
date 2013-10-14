module FSharp.Data.SqlClient.Test

open Xunit
open FsUnit
open System.Configuration

[<Fact>]
let ``Connection string provided`` () = Configuration.getConnectionString ("", "foo", "", "") |> should equal "foo"

[<Fact>]
let ``Nothing is provided`` () =(fun () ->  Configuration.getConnectionString ("", "", "", "") |> ignore) |> should throw typeof<System.Exception>

[<Fact>]
let ``From config file`` () = 
    Configuration.getConnectionString ("", "", "AdventureWorks2012", "Tests.dll.config")
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let ``From default config file`` () = 
    Configuration.getConnectionString ("", "", "AdventureWorks2012", "" )
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString