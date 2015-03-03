module WebApi.DataAccess

open FSharp.Data

[<Literal>]
let AdventureWorks2012 = "name=AdventureWorks2012"

type QueryProducts = SqlCommandProvider<"T-SQL\Products.sql", AdventureWorks2012, DataDirectory = "App_Data">

type AdventureWorks = SqlProgrammabilityProvider<AdventureWorks2012, DataDirectory = "App_Data">

