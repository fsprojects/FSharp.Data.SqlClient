module FSharp.Data.SqlClient.ConfigurationTest

open Xunit
open FsUnit.Xunit
open System.Configuration
open System.IO
open FSharp.Data

type Get42RelativePath = SqlCommandProvider<"select 42 ", "name=AdventureWorks2012", ConfigFile="my.config", ResolutionFolder="MyConfigFolder">
type Get42RootPath = SqlCommandProvider<"select 42 ", "name=AdventureWorks2012", ConfigFile="my.config", ResolutionFolder="""C:\Users\mitekm\Documents\GitHub\FSharp.Data.SqlClient\src\SqlClient.Tests\MyConfigFolder""">

[<Fact>]
let ``Wrong config file name`` () = 
    should throw typeof<FileNotFoundException> <| fun() ->
        Configuration.ReadConnectionStringFromConfigFileByName ( name = "", resolutionFolder = "", fileName = "non_existent") |> ignore

[<Fact>]
let ``From config file`` () = 
    Configuration.ReadConnectionStringFromConfigFileByName(
        name = "AdventureWorks2012", 
        resolutionFolder = __SOURCE_DIRECTORY__,
        fileName = "app.config"
    ) 
    |> should equal ConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

[<Fact>]
let RuntimeConfig () = 
    let name = "AdventureWorks2012"
    Configuration.GetConnectionStringRunTimeByName name
    |> should equal ConfigurationManager.ConnectionStrings.[name].ConnectionString

