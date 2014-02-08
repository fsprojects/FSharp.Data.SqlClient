
#r "../bin/SqlProgrammabilityProvider.Runtime.dll"
#r "../bin/SqlProgrammabilityProvider.dll"

open System
open System.Data
open FSharp.Data.Experimental

[<Literal>] 
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
 

type AdventureWorks2012 = SqlProgrammability<connectionString>

type seType = AdventureWorks2012.``User-Defined Table Types``.SingleElementType

type myType = AdventureWorks2012.``User-Defined Table Types``.MyTableType

let db = AdventureWorks2012()
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

db.``Stored Procedures``.``dbo.MyProc``.AsyncExecute(m) |> Async.RunSynchronously |> Array.ofSeq
