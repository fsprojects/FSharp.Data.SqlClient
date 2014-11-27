    
#r "../../bin/Fsharp.Data.SqlClient.dll"
#r "../../bin/Microsoft.SqlServer.Types.dll"

open System
open System.Data
open FSharp.Data
open Microsoft.SqlServer.Types

[<Literal>] 
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
//let connectionString = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"

[<Literal>] 
let prodConnectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=master;Integrated Security=True"

type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>
type dbo = AdventureWorks2012.dbo
type HumanResources = AdventureWorks2012.HumanResources
type Person = AdventureWorks2012.Person
type Production = AdventureWorks2012.Production
type Purchasing = AdventureWorks2012.Purchasing
type Sales = AdventureWorks2012.Sales

let tt = new HumanResources.Tables.JobCandidate()
let r = tt.NewRow()

let func(r: #DataTable) = ()

type ErrorLog = dbo.Tables.ErrorLog
let t = new ErrorLog()
let r = t.NewRow(Some DateTime.Now, "mitekm", 15, Some 42, ErrorMessage = "haha")
////let r = t.NewRow(DateTime.Now, "mitekm", 15, ErrorMessage = "haha")
//func t
t.Rows.Add r
t.Rows.Count
t.Rows.[0]
//t.AddRow("test2", "group2", DateTime.Now)
//t.Rows.Count
//t.Rows.[1]

let shift = new AdventureWorks2012.HumanResources.Tables.Shift()
shift.Rows.Count
shift.Columns.["ModifiedDate"].DefaultValue.GetType().Name
shift.Columns.["ModifiedDate"].AllowDBNull
shift.Columns.["ShiftID"].AutoIncrement
shift.AddRow("French coffee break", TimeSpan.FromHours(10.), TimeSpan.FromHours(12.))
shift.Rows.Count
shift.Rows.[0]
func shift

//Table-valued UDF selecting single row
type GetContactInformation = dbo.ufnGetContactInformation
let getContactInformation = new GetContactInformation()
getContactInformation.Execute() |> printfn "%A"
let f = getContactInformation.Execute( 1) |> Seq.exactlyOne
f.BusinessEntityType
f.FirstName
f.JobTitle
f.LastName
f.PersonID

//Scalar-Value
type LeadingZeros = dbo.ufnLeadingZeros
let leadingZeros = new LeadingZeros()
leadingZeros.Execute( 12) 

//Stored Procedure returning list of records similar to SqlCommandProvider
type GetWhereUsedProductID = dbo.uspGetWhereUsedProductID
let getWhereUsedProductID = new GetWhereUsedProductID()
getWhereUsedProductID.AsyncExecute(1, DateTime(2013,1,1)) |> Async.RunSynchronously |> Array.ofSeq

//
//UDTT with nullable column
type myType = dbo.MyTableType
let m = [ myType(myId = 2); myType(myId = 1) ]

type MyProc = dbo.MyProc
let myArray = (new MyProc()).AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq

let myRes = myArray.[0]
myRes.myId
myRes.myName

//Call stored procedure to update
type UpdateEmployeeLogin = AdventureWorks2012.HumanResources.uspUpdateEmployeeLogin
let updateEmployeeLogin = new UpdateEmployeeLogin()

let res = updateEmployeeLogin.AsyncExecute()
                291, 
                SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/")),
                "adventure-works\gat0", 
                "gatekeeper", 
                DateTime(2013,1,1), 
                true 
            )
            |> Async.RunSynchronously 

//module DbElephant = 
//    [<Literal>]
//    let local = "Data Source=.;Initial Catalog=dbElephant;Integrated Security=True"
//
//    type Database = SqlProgrammabilityProvider<local>
//    type dbo = Database.dbo
//    
//    type GetAssetTypeId = dbo.AssetTypeId
//    let getAssetTypeId = new GetAssetTypeId()
//
//    type InjectionWellConversion = Database.hw.usp_InjectionWellConversion
//    let injectionWellConversion = new InjectionWellConversion()

//
//    type GetMeasureTypeId = Database.dbo.MeasureTypeId
//    let getMeasureTypeId = new GetMeasureTypeId()
//
//    //type insert = Database.hw.
//
//DbElephant.getMeasureTypeId.AsyncExecute( 1, "Rod Pump", "water cut") |> Async.RunSynchronously
//
//
//
//

//DbElephant.getAssetTypeId.AsyncExecute("Tank", 1) |> Async.RunSynchronously

//let x = new Run