
#r "../../bin/Fsharp.Data.SqlClient.dll"
#r "../../bin/Microsoft.SqlServer.Types.dll"

open System
open System.Data
open FSharp.Data
open Microsoft.SqlServer.Types

[<Literal>] 
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

[<Literal>] 
let prodConnectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=master;Integrated Security=True"

type AdventureWorks2012 = SqlProgrammabilityProvider<connectionString>

let db = AdventureWorks2012()

//Table-valued UDF selecting single row
let f = db.Functions.``dbo.ufnGetContactInformation``.AsyncExecute(1) |> Async.RunSynchronously |> Seq.exactlyOne
f.BusinessEntityType
f.FirstName
f.JobTitle
f.LastName
f.PersonID

//Stored Procedure returning list of records similar to SqlCommandProvider
db.``Stored Procedures``.``dbo.uspGetWhereUsedProductID``.AsyncExecute(1, DateTime(2013,1,1)) |> Async.RunSynchronously |> Array.ofSeq

//Mix of input and output parameters in SP
let a = db.``Stored Procedures``.``dbo.Swap``.AsyncExecute(input=5) |> Async.RunSynchronously 
a.output
a.nullStringOutput
a.ReturnValue
a.nullOutput

//UDTT with nullable column
type myType = AdventureWorks2012.``User-Defined Table Types``.MyTableType
let m = [ myType(myId = 2); myType(myId = 1) ]

let myArray = db.``Stored Procedures``.``dbo.MyProc``.AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq

let myRes = myArray.[0]
myRes.myId
myRes.myName

//Call stored procedure to update
let res = db.``Stored Procedures``.``HumanResources.uspUpdateEmployeeLogin``
            .AsyncExecute(
                291, 
                SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/")),
                "adventure-works\gat0", 
                "gatekeeper", 
                DateTime(2013,1,1), 
                true 
            )
            |> Async.RunSynchronously 
res.ReturnValue





