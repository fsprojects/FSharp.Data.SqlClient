module DesignTimeConnectionStringTests

open Xunit
open System.IO
open System.Collections.Generic
open System.Configuration
open FSharp.Data.SqlClient

let adventureWorks =
    // Read via DesignTimeConnectionString.Parse so the same code path works on both
    // .NET Framework and .NET Core (where ConfigurationManager.ConnectionStrings is
    // not automatically populated in test host processes).
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    x.Value

// ---- Literal connection string tests (no SQL Server or config file needed) ----

[<Fact>]
let ``Literal connection string returns Literal case`` () =
    let cs = "Data Source=.;Initial Catalog=Test;Integrated Security=True"
    let x = DesignTimeConnectionString.Parse(cs, resolutionFolder = "", fileName = "")
    match x with
    | Literal value -> Assert.Equal<string>(cs, value)
    | _ -> failwith "Expected Literal"

[<Fact>]
let ``Literal IsDefinedByLiteral is true`` () =
    let x = DesignTimeConnectionString.Parse("Server=.;Database=Test", resolutionFolder = "", fileName = "")
    Assert.True(x.IsDefinedByLiteral)

[<Fact>]
let ``Literal Value returns the connection string`` () =
    let cs = "Server=.;Database=Northwind"
    let x = DesignTimeConnectionString.Parse(cs, resolutionFolder = "", fileName = "")
    Assert.Equal<string>(cs, x.Value)

[<Fact>]
let ``Literal RunTimeValueExpr evaluates to the original string`` () =
    let cs = "Server=.;Database=Northwind"
    let x = DesignTimeConnectionString.Parse(cs, resolutionFolder = "", fileName = "")
    let actual =
        Linq.RuntimeHelpers.LeafExpressionConverter.EvaluateQuotation(x.RunTimeValueExpr(isHostedExecution = false))
        |> unbox<string>
    Assert.Equal<string>(cs, actual)

[<Fact>]
let ``name= prefix with spaces is still parsed as NameInConfig`` () =
    // "name = X" (with spaces around =) should be treated the same as "name=X"
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    let xSpaced = DesignTimeConnectionString.Parse("name = AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    Assert.Equal<string>(x.Value, xSpaced.Value)

[<Fact>]
let ``Missing connection name in config throws KeyNotFoundException`` () =
    Assert.Throws<KeyNotFoundException>(fun () ->
        DesignTimeConnectionString.Parse("name=DoesNotExistInConfig", __SOURCE_DIRECTORY__, "app.config") |> box
    ) |> ignore

// ---- Config-file-based tests (require app.config in test directory) ----

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
let ``NameInConfig IsDefinedByLiteral is false`` () =
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    Assert.False(x.IsDefinedByLiteral)

[<Fact>]
let ``NameInConfig Value returns the connection string from config`` () =
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    Assert.Equal<string>(adventureWorks, x.Value)

[<Fact>]
let RuntimeConfig() = 
    let x = DesignTimeConnectionString.Parse("name=AdventureWorks", __SOURCE_DIRECTORY__, "app.config")
    let actual = Linq.RuntimeHelpers.LeafExpressionConverter.EvaluateQuotation( x.RunTimeValueExpr(isHostedExecution = false)) |> unbox
    Assert.Equal<string>( adventureWorks, actual)


