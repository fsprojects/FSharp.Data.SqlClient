module FSharp.Data.Experimental.Internals.ConfigurationTest

open Xunit
open FsUnit.Xunit
open System.Configuration
open System.Reflection
open System
open System.IO

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
    let assemblyName = 
        Uri(Assembly.GetExecutingAssembly().GetName().CodeBase).LocalPath
        |> Path.GetFileName

    Configuration.GetConnectionString(
        resolutionFolder = "", 
        connectionString = "", 
        connectionStringName = "AdventureWorks2012", 
        //configFile = Assembly.GetExecutingAssembly().GetName().CodeBase + ".config"
        configFile = assemblyName + ".config"
    ) 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let ``From default config file`` () = 
    Configuration.GetConnectionString(resolutionFolder = "", connectionString = "", connectionStringName = "AdventureWorks2012", configFile = "") 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString