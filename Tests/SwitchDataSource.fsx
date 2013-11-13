#I @"../packages/FAKE/tools"
#r @"../packages/FAKE/tools/FakeLib.dll"

open System
open Fake 

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let oldSource = "Data Source=.;"
let newSource = """Data Source=(LocalDb)\v11.0;"""
//let oldSource = """Data Source=(LocalDb)\v11.0;"""
//let newSource = "Data Source=.;"
let connectionString = newSource + "Initial Catalog=AdventureWorks2012;Integrated Security=True"

[
    "Databinding/app.config"
    "WebApi/web.config"
    "app.config"
] |> Seq.iter(fun s -> ConfigurationHelper.updateConnectionString "AdventureWorks2012" connectionString s)

[
    "Test.fsx"
    "TypeProvider.Test.fs"
] |> Seq.iter (fun s-> StringHelper.ReplaceInFile (fun s-> s.Replace(oldSource, newSource)) s)




