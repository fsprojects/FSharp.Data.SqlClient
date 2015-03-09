module FSharp.Data.SqlClient.ConfigurationTests

open Xunit
open FsUnit.Xunit
open System.Configuration
open System.IO
open FSharp.Data

[<Fact>]
let ``Wrong config file name`` () = 
    should throw typeof<FileNotFoundException> <| fun() ->
        Configuration.ReadConnectionStringFromConfigFileByName ( name = "", resolutionFolder = "", fileName = "non_existent") |> ignore

[<Fact>]
let ``From config file`` () = 
    Configuration.ReadConnectionStringFromConfigFileByName(
        name = "AdventureWorks", 
        resolutionFolder = __SOURCE_DIRECTORY__,
        fileName = "app.config"
    ) 
    |> should equal Settings.ConnectionStrings.AdventureWorks

[<Fact>]
let RuntimeConfig () = 
    Configuration.GetConnectionStringAtRunTime "AdventureWorks"
    |> should equal Settings.ConnectionStrings.AdventureWorks

[<Fact>]
let CheckValidFileName() = 
    let expected = Some "c:\\mysqlfiles\\test.sql"
    Configuration.GetValidFileName("test.sql", "c:\\mysqlfiles") |> should equal expected
    Configuration.GetValidFileName("../test.sql", "c:\\mysqlfiles\\subfolder") |> should equal expected
    Configuration.GetValidFileName("c:\\mysqlfiles/test.sql", "d:\\otherdrive") |> should equal expected
    Configuration.GetValidFileName("../mysqlfiles/test.sql", "c:\\otherfolder") |> should equal expected
    Configuration.GetValidFileName("a/b/c/../../../test.sql", "c:\\mysqlfiles") |> should equal expected

type Get42RelativePath = SqlCommandProvider<"sampleCommand.sql", ConnectionStrings.AdventureWorksNamed, ResolutionFolder="MySqlFolder">

type Get42 = SqlCommandProvider<"SELECT 42", ConnectionStrings.AdventureWorksNamed, ConfigFile = "appWithInclude.config">

//[<Literal>]
//let longQueryText = "INSERT INTO [HumanResources].[Employee] ([BusinessEntityID], [NationalIDNumber], [LoginID], [OrganizationNode], [JobTitle], [BirthDate], [MaritalStatus], [Gender], [HireDate], [SalariedFlag], [VacationHours], [SickLeaveHours], [CurrentFlag], [rowguid], [ModifiedDate]) VALUES (1, N'295847284', N'adventure-works\ken0', N'/', N'Chief Executive Officer', N'1963-03-02', N'S', N'M', N'2003-02-15', 1, 99, 69, 1, N'f01251e5-96a3-448d-981e-0f99d789110d', N'2008-07-31 00:00:00')" 

[<Literal>]
let longQueryText = "INSERT INTO Production.BillOfMaterials (BillOfMaterialsID, ProductAssemblyID, ComponentID, StartDate, EndDate, UnitMeasureCode, BOMLevel, PerAssemblyQty, ModifiedDate) VALUES (1692, 776, 907, N'2004-07-20 00:00:00', NULL, N'EA ', 1, CAST(1.00 AS Decimal(8, 2)), N'2004-07-06 00:00:00')"
type LongQuery = SqlCommandProvider<longQueryText, ConnectionStrings.AdventureWorksNamed>