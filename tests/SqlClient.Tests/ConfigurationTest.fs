module FSharp.Data.SqlClient.ConfigurationTests 

open Xunit
open FSharp.Data
open System.Configuration

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
