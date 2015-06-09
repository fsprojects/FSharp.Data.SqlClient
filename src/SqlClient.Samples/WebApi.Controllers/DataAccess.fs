module WebApi.DataAccess

open FSharp.Data

[<Literal>]
let AdventureWorks = "name=AdventureWorks"

type QueryProducts = SqlCommandProvider<"T-SQL/Products.sql", AdventureWorks, DataDirectory = "App_Data">

type AdventureWorks = SqlProgrammabilityProvider<AdventureWorks, DataDirectory = "App_Data">

