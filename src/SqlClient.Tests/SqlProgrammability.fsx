
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

type Functions = AdventureWorks2012.Functions
type StoredProcedures = AdventureWorks2012.``Stored Procedures``

//Table-valued UDF selecting single row
type GetContactInformation = Functions.``dbo.ufnGetContactInformation``
let getContactInformation = new GetContactInformation()
getContactInformation.Execute() |> printfn "%A"
let f = getContactInformation.Execute( 1) |> Seq.exactlyOne
f.BusinessEntityType
f.FirstName
f.JobTitle
f.LastName
f.PersonID

//Scalar-Value
type LeadingZeros = Functions.``dbo.ufnLeadingZeros``
let leadingZeros = new LeadingZeros()
leadingZeros.Execute( 12) 

//Stored Procedure returning list of records similar to SqlCommandProvider
type GetWhereUsedProductID = StoredProcedures.``dbo.uspGetWhereUsedProductID``
let getWhereUsedProductID = new GetWhereUsedProductID()
getWhereUsedProductID.AsyncExecute(1, DateTime(2013,1,1)) |> Async.RunSynchronously |> Array.ofSeq

//Mix of input and output parameters in SP
//type Swap = StoredProcedures.``dbo.Swap``
//let swap = new Swap() 
//swap.Execute(input=5) |> Async.RunSynchronously 
//a.output
//a.nullStringOutput
//a.ReturnValue
//a.nullOutput

//UDTT with nullable column
type myType = AdventureWorks2012.``User-Defined Table Types``.MyTableType
let m = [ myType(myId = 2); myType(myId = 1) ]

type MyProc = AdventureWorks2012.``Stored Procedures``.``dbo.MyProc``
let myArray = (new MyProc()).AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq

let myRes = myArray.[0]
myRes.myId
myRes.myName

//Call stored procedure to update
type UpdateEmployeeLogin = StoredProcedures.``HumanResources.uspUpdateEmployeeLogin``
let updateEmployeeLogin = new UpdateEmployeeLogin()

let res = updateEmployeeLogin.AsyncExecute(
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
//    Database.``User-Defined Table Types``.IntList
//    type Functions = Database.dbo.Functions
//    type StoredProcedures = Database.``Stored Procedures``
//    
//    type GetMeasureTypeId = Functions.``dbo.MeasureTypeId``
//    let getMeasureTypeId = new GetMeasureTypeId()
//
//DbElephant.getMeasureTypeId.AsyncExecute( 1, "Rod Pump", "water cut") |> Async.RunSynchronously
//



