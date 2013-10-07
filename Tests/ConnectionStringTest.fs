module  FSharp.Data.SqlClient.Test

open Xunit
open FsUnit
open System.Configuration

[<Fact>]
let ``Connection string provided`` () = ConnectionString.resolve "" "foo" "" "" |> should equal "foo"

[<Fact>]
let ``Nothing is provided`` () =(fun () ->  ConnectionString.resolve "" "" "" "" |> ignore) |> should throw typeof<System.Exception>

[<Fact>]
let ``From config file`` () = 
    ConnectionString.resolve "" "" "AdventureWorks2012" "Tests.dll.config" 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let ``From default config file`` () = 
    ConnectionString.resolve "" "" "AdventureWorks2012" "" 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString