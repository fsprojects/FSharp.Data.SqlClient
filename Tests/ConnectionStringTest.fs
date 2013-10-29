module FSharp.Data.SqlClient.Test

open Xunit
open FsUnit
open System.Configuration

[<Fact>]
let ``Connection string provided`` () = Configuration.getConnectionString ""  "foo"  ""  ""  |> should equal "foo"

[<Fact>]
let ``Nothing is provided`` () = 
    should throw typeof<System.Exception> <| fun() -> Configuration.getConnectionString ""  ""  ""  "" |> ignore

[<Fact>]
let ``From config file`` () = 
    Configuration.getConnectionString "" "" "AdventureWorks2012" "Tests.dll.config"
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let ``From default config file`` () = 
    Configuration.getConnectionString "" "" "AdventureWorks2012" "" 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString