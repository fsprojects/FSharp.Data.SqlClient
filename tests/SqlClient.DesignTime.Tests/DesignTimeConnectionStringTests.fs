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

// ---- Edge case tests for Parse ----

[<Fact>]
let ``Empty string throws ArgumentException`` () =
    // The [| "" |] guard in Parse must fire — not be skipped by RemoveEmptyEntries.
    Assert.Throws<System.ArgumentException>(
        fun () -> DesignTimeConnectionString.Parse("", resolutionFolder = "", fileName = "") |> box
    ) |> ignore

[<Fact>]
let ``Whitespace-only string throws ArgumentException`` () =
    Assert.Throws<System.ArgumentException>(
        fun () -> DesignTimeConnectionString.Parse("   ", resolutionFolder = "", fileName = "") |> box
    ) |> ignore

[<Fact>]
let ``Literal connection string is parsed correctly`` () =
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
