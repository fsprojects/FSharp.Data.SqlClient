#I @"..\..\..\packages"
#r "../../../bin/Fsharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types.dll"
//#load "ConnectionStrings.fs"
open System
open System.Data
open FSharp.Data


//[<Literal>] 
//let connectionString = ConnectionStrings.AdventureWorksLiteral
//let connectionString = ConnectionStrings.AdventureWorksAzure

//[<Literal>] 
//let prodConnectionString = ConnectionStrings.MasterDb

type AdventureWorks = SqlProgrammabilityProvider<"Data Source=.;Initial Catalog = AdventureWorks2012;Integrated Security=True">
type dbo = AdventureWorks.dbo
let x = 42.<AdventureWorks.Sales.``Units of Measure``.GBP>
let y = 12.<AdventureWorks.Sales.``Units of Measure``.USD>
x.GetType().AssemblyQualifiedName

//let cmd = new SqlCommandProvider<"
//    SELECT X.* 
//    FROM Sales.SpecialOfferProduct X
//	    JOIN Sales.SalesOrderDetail Y ON X.ProductID = Y.ProductID 
//    WHERE X.ProductID = @specialOfferProductProductid 
//	    AND Y.ProductID = @salesOrderDetailProductid
//	    AND (X.SpecialOfferID IS NOT NULL 
//		    OR Y.SpecialOfferID IS NOT NULL)
//	     ", "Data Source=.;Initial Catalog = AdventureWorks2012;Integrated Security=True">()

//#r @"Newtonsoft.Json.8.0.2\lib\net45\Newtonsoft.Json.dll"
//
//open Newtonsoft.Json
//
////let dt = new DataTable()
////dt.Columns.Add("id", typeof<int>) |> ignore
////dt.Columns.Add("name", typeof<string>) |> ignore
////dt.LoadDataRow([|box 1; box "Roger Federer"|], true) |> ignore
////dt.LoadDataRow([|box 2; box "Rafael Nadal"|], true) |> ignore
////JsonConvert.SerializeObject dt
//
//let dt = new AdventureWorks.HumanResources.Tables.Shift()
//dt.AddRow("French coffee break", StartTime = TimeSpan.FromHours 10., EndTime = TimeSpan.FromHours 12., ModifiedDate = Some DateTime.Now)
//dt.AddRow("Spanish siesta", TimeSpan.FromHours 13., TimeSpan.FromHours 16., Some DateTime.Now)
//let s = JsonConvert.SerializeObject dt
//
//let dt2 = JsonConvert.DeserializeObject<DataTable>(s) //|> box |> unbox<AdventureWorks.HumanResources.Tables.Shift>
//let dt3 = new AdventureWorks.HumanResources.Tables.Shift()
//
//let reader = dt2.CreateDataReader()
//while reader.Read() do ()
//reader.Close()
//
//dt.Merge(dt2)

//[ for c in dt2.Columns -> c.ColumnName, c.DataType.FullName ] = [ for c in dt.Columns -> c.ColumnName, c.DataType.FullName ]

type Thermion = SqlProgrammabilityProvider<"Data Source=.;Initial Catalog = Thermion;Integrated Security=True">


