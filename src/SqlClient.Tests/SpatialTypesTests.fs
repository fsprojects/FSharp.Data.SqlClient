module FSharp.Data.SpatialTypesTests

open Xunit
open Microsoft.SqlServer.Types
open System.Data.SqlTypes

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type GetEmployeeByLevel = SqlCommandProvider<"SELECT OrganizationNode FROM HumanResources.Employee WHERE OrganizationNode = @OrganizationNode", connectionString, SingleRow = true>
[<Fact>]
let SqlHierarchyIdParam() =    
    let getEmployeeByLevel = new GetEmployeeByLevel()
    let p = SqlHierarchyId.Parse(SqlString("/1/1/"))
    let result = getEmployeeByLevel.Execute(p)
    Assert.Equal(Some(Some p), result)
