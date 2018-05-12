module FSharp.Data.SqlClient.ConfigurationTests 

#if DEBUG
#nowarn "101"
#endif

open Xunit
open System.Configuration
open System.IO
open FSharp.Data

let adventureWorks = ConfigurationManager.ConnectionStrings.["AdventureWorks"].ConnectionString

type Get42 = SqlCommandProvider<"SELECT 42", ConnectionStrings.AdventureWorksNamed, ConfigFile = "appWithInclude.config">

type LongQuery = SqlCommandProvider<"
    INSERT INTO Production.BillOfMaterials (BillOfMaterialsID, ProductAssemblyID, ComponentID, StartDate, EndDate, UnitMeasureCode, BOMLevel, PerAssemblyQty, ModifiedDate) 
    VALUES (1692, 776, 907, N'2004-07-20 00:00:00', NULL, N'EA ', 1, CAST(1.00 AS Decimal(8, 2)), N'2004-07-06 00:00:00')
", ConnectionStrings.AdventureWorksNamed>

type SampleCommand = SqlFile<"sampleCommand.sql">
type SampleCommandRelative = SqlFile<"sampleCommand.sql", "MySqlFolder">

[<Fact>]
let SqlFiles() = 
    use cmd1 = new SqlCommandProvider<SampleCommand.Text, ConnectionStrings.AdventureWorksNamed>()
    use cmd2 = new SqlCommandProvider<SampleCommandRelative.Text, ConnectionStrings.AdventureWorksNamed>()
    Assert.Equal<_ seq>(cmd1.Execute() |> Seq.toArray, cmd2.Execute() |> Seq.toArray)

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

