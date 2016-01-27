module FSharp.Data.SqlClient.ConfigurationTests 

open Xunit
open System.Configuration
open System.IO
open FSharp.Data

let adventureWorks = FSharp.Configuration.AppSettings<"app.config">.ConnectionStrings.AdventureWorks

[<Fact>]
let CheckValidFileName() = 
    let expected = Some "c:\\mysqlfiles\\test.sql"
    Assert.Equal(expected, Configuration.GetValidFileName("test.sql", "c:\\mysqlfiles"))

    Assert.Equal(expected, Configuration.GetValidFileName("test.sql", "c:\\mysqlfiles"))
    Assert.Equal(expected, Configuration.GetValidFileName("../test.sql", "c:\\mysqlfiles\\subfolder"))
    Assert.Equal(expected, Configuration.GetValidFileName("c:\\mysqlfiles/test.sql", "d:\\otherdrive"))
    Assert.Equal(expected, Configuration.GetValidFileName("../mysqlfiles/test.sql", "c:\\otherfolder"))
    Assert.Equal(expected, Configuration.GetValidFileName("a/b/c/../../../test.sql", "c:\\mysqlfiles"))

type Get42RelativePath = SqlCommandProvider<"sampleCommand.sql", ConnectionStrings.AdventureWorksNamed, ResolutionFolder="MySqlFolder">

type Get42 = SqlCommandProvider<"SELECT 42", ConnectionStrings.AdventureWorksNamed, ConfigFile = "appWithInclude.config">

type LongQuery = SqlCommandProvider<"
    INSERT INTO Production.BillOfMaterials (BillOfMaterialsID, ProductAssemblyID, ComponentID, StartDate, EndDate, UnitMeasureCode, BOMLevel, PerAssemblyQty, ModifiedDate) 
    VALUES (1692, 776, 907, N'2004-07-20 00:00:00', NULL, N'EA ', 1, CAST(1.00 AS Decimal(8, 2)), N'2004-07-06 00:00:00')
", ConnectionStrings.AdventureWorksNamed>

#if DEBUG
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

#endif

