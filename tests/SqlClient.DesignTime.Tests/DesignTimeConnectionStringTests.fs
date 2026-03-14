module DesignTimeConnectionStringTests

open Xunit
open System.IO
open System.Configuration
open FSharp.Data.SqlClient

let adventureWorks = ConfigurationManager.ConnectionStrings.["AdventureWorks"].ConnectionString

[<Fact>]
let ``Wrong config file name`` () = 
    Assert.Throws<FileNotFoundException>(
        fun() -> 
            DesignTimeConnectionString.Parse("name=AdventureWorks", resolutionFolder = "", fileName = "non_existent") |> box
    ) |> ignore

[<Fact>]
let ``From config file`` () = 
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    match x with
    | NameInConfig(name, value, _) ->
        Assert.Equal<string>("AdventureWorks", name)
        Assert.Equal<string>(adventureWorks, value)
    | _ -> failwith "Unexpected"

[<Fact>]
let RuntimeConfig() = 
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    let actual = Linq.RuntimeHelpers.LeafExpressionConverter.EvaluateQuotation( x.RunTimeValueExpr(isHostedExecution = false)) |> unbox
    Assert.Equal<string>( adventureWorks, actual)

// appsettings.json support tests

[<Fact>]
let ``TryReadFromAppSettings - found connection string`` () =
    let result = DesignTimeConnectionString.TryReadFromAppSettings("AdventureWorks", Path.Combine(__SOURCE_DIRECTORY__, "appsettings.json"))
    Assert.Equal(Some adventureWorks, result)

[<Fact>]
let ``TryReadFromAppSettings - missing connection string returns None`` () =
    let result = DesignTimeConnectionString.TryReadFromAppSettings("NonExistent", Path.Combine(__SOURCE_DIRECTORY__, "appsettings.json"))
    Assert.Equal(None, result)

[<Fact>]
let ``TryReadFromAppSettings - missing file returns None`` () =
    let result = DesignTimeConnectionString.TryReadFromAppSettings("AdventureWorks", Path.Combine(__SOURCE_DIRECTORY__, "not_there.json"))
    Assert.Equal(None, result)

[<Fact>]
let ``From explicit appsettings.json file`` () =
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "appsettings.json")
    match x with
    | NameInConfig(name, value, _) ->
        Assert.Equal<string>("AdventureWorks", name)
        Assert.Equal<string>(adventureWorks, value)
    | _ -> failwith "Expected NameInConfig"

[<Fact>]
let ``Auto-discovery finds appsettings.json when no xml config present`` () =
    let tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    Directory.CreateDirectory(tempDir) |> ignore
    try
        let jsonContent = """{"ConnectionStrings":{"TestDb":"Server=localhost;Database=Test"}}"""
        File.WriteAllText(Path.Combine(tempDir, "appsettings.json"), jsonContent)
        let x = DesignTimeConnectionString.Parse("name=TestDb", tempDir, "")
        match x with
        | NameInConfig(name, value, _) ->
            Assert.Equal<string>("TestDb", name)
            Assert.Equal<string>("Server=localhost;Database=Test", value)
        | _ -> failwith "Expected NameInConfig"
    finally
        Directory.Delete(tempDir, recursive = true)

[<Fact>]
let ``No config file gives helpful error`` () =
    let tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    Directory.CreateDirectory(tempDir) |> ignore
    try
        let ex = Assert.Throws<Exception>(fun () ->
            DesignTimeConnectionString.Parse("name=MyDb", tempDir, "") |> ignore)
        Assert.Contains("appsettings.json", ex.Message)
    finally
        Directory.Delete(tempDir, recursive = true)
