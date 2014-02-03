
#r "../bin/SqlProgrammabilityProvider.Runtime.dll"
#r "../bin/SqlProgrammabilityProvider.dll"

open System
open System.Data
open FSharp.Data.Experimental

[<Literal>] 
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
 

type AdventureWorks2012 = SqlProgrammability<connectionString>

let db = AdventureWorks2012()
db.StoredProcedures.``HumanResources.uspUpdateEmployeeHireInfo``.AsyncExecute()
