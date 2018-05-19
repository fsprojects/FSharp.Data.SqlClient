module WebApi.DataAccess

open FSharp.Data

[<Literal>]
let AdventureWorks = "name=AdventureWorks"

type QueryProducts = SqlCommandProvider< const(SqlFile<"T-SQL/Products.sql">.Text), AdventureWorks, DataDirectory = "App_Data">

//type ProductQuery = SqlFile<"T-SQL/Products.sql">
//type QueryProducts = SqlCommandProvider<ProductQuery.Text, AdventureWorks, DataDirectory = "App_Data">

type AdventureWorks = SqlProgrammabilityProvider<AdventureWorks, DataDirectory = "App_Data">

