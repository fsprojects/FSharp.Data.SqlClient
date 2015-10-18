module FSharp.Data.SqlClient.ConfigurationTests 

open Xunit
open System.Configuration
open System.IO
open FSharp.Data

let adventureWorks = FSharp.Configuration.AppSettings<"app.config">.ConnectionStrings.AdventureWorks

[<Fact>]
let ``Wrong config file name`` () = 
    let connStr = ConnectionString.NameInConfig ""
    Assert.Throws<FileNotFoundException>(fun() -> connStr.GetDesignTimeValueAndProvider(resolutionFolder = "", fileName = "non_existent") |> box) |> ignore

[<Fact>]
let ``From config file`` () = 
    let connStr = ConnectionString.NameInConfig "AdventureWorks"
    let connStr, _ = connStr.GetDesignTimeValueAndProvider(resolutionFolder = __SOURCE_DIRECTORY__,  fileName = "app.config") 
    Assert.Equal<string>(adventureWorks, connStr)

[<Fact>]
let RuntimeConfig() = 
    let connStr = ConnectionString.NameInConfig "AdventureWorks"
    Assert.Equal<string>(expected = adventureWorks, actual = connStr.Value)

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
