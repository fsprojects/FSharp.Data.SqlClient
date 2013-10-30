module FSharp.Data.SqlClient.Test

open Xunit
open FsUnit
open System.Configuration

[<Fact>]
let ``Connection string provided`` () = 
    Configuration.GetConnectionString(resolutionFolder = "", connectionString = "foo", connectionStringName = "", configFile = "")  
    |> should equal "foo"

[<Fact>]
let ``Nothing is provided`` () = 
    should throw typeof<System.Exception> <| fun() ->
        Configuration.GetConnectionString(resolutionFolder = "", connectionString = "", connectionStringName = "", configFile = "") 
        |> ignore

[<Fact>]
let ``From config file`` () = 
    Configuration.GetConnectionString(resolutionFolder = "", connectionString = "", connectionStringName = "AdventureWorks2012", configFile = "Tests.dll.config") 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let ``From default config file`` () = 
    Configuration.GetConnectionString(resolutionFolder = "", connectionString = "", connectionStringName = "AdventureWorks2012", configFile = "") 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString