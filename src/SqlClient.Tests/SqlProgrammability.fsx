
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

type seType = AdventureWorks2012.``User-Defined Table Types``.SingleElementType

type myType = AdventureWorks2012.``User-Defined Table Types``.MyTableType

let db = AdventureWorks2012()

let f = db.Functions.``dbo.ufnGetContactInformation``.AsyncExecute(1) |> Async.RunSynchronously |> Seq.exactlyOne
f.BusinessEntityType
f.FirstName
f.JobTitle
f.LastName
f.PersonID

let a = db.``Stored Procedures``.``dbo.Swap``.AsyncExecute(input=5) |> Async.RunSynchronously 
a.output
a.nullStringOutput
a.ReturnValue
a.nullOutput

db.``Stored Procedures``.``dbo.uspGetWhereUsedProductID``.AsyncExecute(DateTime(2013,1,1), 1) |> Async.RunSynchronously |> Array.ofSeq
    
let p = [ 
    seType(myId = 2)
    seType(myId = 1) 
]

db.``Stored Procedures``.``dbo.SingleElementProc``.AsyncExecute(p) |> Async.RunSynchronously |> Array.ofSeq

let m = [ 
    myType(myId = 2)
    myType(myId = 1) 
]

let myArray = db.``Stored Procedures``.``dbo.MyProc``.AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq

let myRes = myArray.[0]
myRes.myId
myRes.myName

let res = db.``Stored Procedures``.``HumanResources.uspUpdateEmployeeLogin``
            .AsyncExecute(291, true, DateTime(2013,1,1), "gatekeeper", "adventure-works\gat0", SqlHierarchyId.Parse(SqlTypes.SqlString("/1/4/2/")))
            |> Async.RunSynchronously 
res.ReturnValue
