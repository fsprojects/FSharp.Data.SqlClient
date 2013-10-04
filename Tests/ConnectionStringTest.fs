module FSharp.NYC.Tutorial.Test

open Xunit
open FsUnit
open System.Configuration
open FSharp.NYC.Tutorial

[<Fact>]
let ``Connection string provided`` () = ConnectionString.resolve "" "foo" "" "" "" |> should equal "foo"

[<Fact>]
let ``Nothing is provided`` () =(fun () ->  ConnectionString.resolve "" "" "" "" "" |> ignore) |> should throw typeof<System.Exception>

[<Fact>]
let ``From config file`` () = 
    ConnectionString.resolve "" "" "AdventureWorks2012" "Tests.dll.config" "" 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString