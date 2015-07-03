module FSharp.Data.ConnectionStrings 

[<Literal>]
let server = @"."

[<Literal>]
let AdventureWorksLiteral = @"Data Source=" + server + ";Initial Catalog=AdventureWorks2014;Integrated Security=True"
[<Literal>]
let AdventureWorksLiteralMultipleActiveResults = AdventureWorksLiteral + ";MultipleActiveResultSets=True"
[<Literal>]
let AdventureWorksNamed = @"name=AdventureWorks"
[<Literal>]
let MasterDb = @"name=MasterDb"
[<Literal>]
let LocalHost = @"Data Source=" + server + ";Integrated Security=True"

let AdventureWorks = FSharp.Data.SqlClient.Configuration.GetConnectionStringAtRunTime "AdventureWorks" 
