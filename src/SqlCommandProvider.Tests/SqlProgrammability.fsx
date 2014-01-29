
#r @"C:\Users\mitekm\Documents\GitHub\FSharp.Data.Experimental.SqlCommandProvider\src\SqlProgrammability\bin\Debug\FSharp.Data.Experimental.SqlProgrammability.dll"

open System
open System.Data
open FSharp.Data.Experimental

[<Literal>] 
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
 

type AdventureWorks2012 = SqlProgrammability<connectionString>

let db = AdventureWorks2012.StoredProcedures.``HumanResources.uspUpdateEmployeeHireInfo``()

