#r "../../bin/Fsharp.Data.SqlClient.dll"
#r "../../bin/Microsoft.SqlServer.Types.dll"
#load "ConnectionStrings.fs"
open System
open System.Data
open FSharp.Data


[<Literal>] 
let connectionString = ConnectionStrings.AdventureWorksLiteral
//let connectionString = ConnectionStrings.AdventureWorksAzure

[<Literal>] 
let prodConnectionString = ConnectionStrings.MasterDb

//type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>
//type dbo = AdventureWorks2012.dbo
//type HumanResources = AdventureWorks2012.HumanResources
//type Person = AdventureWorks2012.Person
//type Production = AdventureWorks2012.Production
//type Purchasing = AdventureWorks2012.Purchasing
//type Sales = AdventureWorks2012.Sales
//
////(new Person.Address_GetAddressBySpatialLocation()).Execute()
//
//let tt = new HumanResources.Tables.JobCandidate()
//let r = tt.NewRow()
//
//let func(r: #DataTable) = ()
//
//type ErrorLog = dbo.Tables.ErrorLog
//let t = new ErrorLog()
//let row = t.NewRow("mitekm", 15, ErrorMessage = "haha", ErrorTime = Some DateTime.Now, ErrorSeverity = Some 42)
//////let r = t.NewRow(DateTime.Now, "mitekm", 15, ErrorMessage = "haha")
////func t
//t.Rows.Add row
//t.Rows.Count
//t.Rows.[0]
////t.AddRow("test2", "group2", DateTime.Now)
////t.Rows.Count
////t.Rows.[1]
//
//let shift = new AdventureWorks2012.HumanResources.Tables.Shift()
//shift.Rows.Count
//shift.Columns.["ModifiedDate"].DefaultValue.GetType().Name
//shift.Columns.["ModifiedDate"].AllowDBNull
//shift.Columns.["ShiftID"].AutoIncrement
//shift.AddRow("French coffee break", TimeSpan.FromHours(10.), TimeSpan.FromHours(12.))
//shift.Rows.Count
//shift.Rows.[0]
//func shift
//
////Table-valued UDF selecting single row
//type GetContactInformation = dbo.ufnGetContactInformation
//let getContactInformation = new GetContactInformation()
//getContactInformation.Execute(1) |> printfn "%A"
//let f = getContactInformation.Execute( 1) |> Seq.exactlyOne
//f.BusinessEntityType
//f.FirstName
//f.JobTitle
//f.LastName
//f.PersonID
//
////Scalar-Value
//type LeadingZeros = dbo.ufnLeadingZeros
//let leadingZeros = new LeadingZeros()
//leadingZeros.Execute(12) 
//
////Stored Procedure returning list of records similar to SqlCommandProvider
//type GetWhereUsedProductID = dbo.uspGetWhereUsedProductID
//let getWhereUsedProductID = new GetWhereUsedProductID()
//getWhereUsedProductID.AsyncExecute(1, DateTime(2013,1,1)) |> Async.RunSynchronously |> Array.ofSeq
//
////
////UDTT with nullable column
//type myType = Person.``User-Defined Table Types``.MyTableType
//let m = [ myType(myId = 2); myType(myId = 1) ]
//
//type MyProc = Person.MyProc
//let myArray = (new MyProc()).AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq
//
//let myRes = myArray.[0]
//myRes.myId
//myRes.myName
//
////Call stored procedure to update
//type UpdateEmployeeLogin = AdventureWorks2012.HumanResources.uspUpdateEmployeeLogin
//let updateEmployeeLogin = new UpdateEmployeeLogin()
//
//let res = updateEmployeeLogin.AsyncExecute(
//                291, 
//                Microsoft.SqlServer.Types.SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/")),
//                "adventure-works\gat0", 
//                "gatekeeper", 
//                DateTime(2013,1,1), 
//                true 
//            )
//            |> Async.RunSynchronously 
//

let cmd42 = new SqlProgrammabilityProvider<connectionString>.dbo.Get42()
cmd42.ExecuteSingle()
cmd42.Execute() |> Seq.exactlyOne
cmd42.AsyncExecuteSingle() |> Async.RunSynchronously
cmd42.AsyncExecute() |> Async.RunSynchronously |> Seq.exactlyOne

