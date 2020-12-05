#if WITH_LEGACY_NAMESPACE
module FSharp.Data.SpatialTypesTests
open FSharp.Data.SqlClient
#else
module FSharp.Data.SqlClient.SpatialTypesTests
#endif

open Xunit
open Microsoft.SqlServer.Types
open System.Data.SqlTypes

[<Literal>]
let connectionString = @"name=AdventureWorks"

type GetEmployeeByLevel = SqlCommandProvider<"SELECT OrganizationNode FROM HumanResources.Employee WHERE OrganizationNode = @OrganizationNode", connectionString, SingleRow = true>

[<Fact>]
let SqlHierarchyIdParam() =    
    let getEmployeeByLevel = new GetEmployeeByLevel()
    let p = SqlHierarchyId.Parse(SqlString("/1/1/"))
    let result = getEmployeeByLevel.Execute(p)
    Assert.Equal(Some(Some p), result)

type AdventureWorks = SqlProgrammabilityProvider<connectionString>
type Address_GetAddressBySpatialLocation = AdventureWorks.Person.Address_GetAddressBySpatialLocation
    
[<Fact>]
let ``GEOMETRY and GEOGRAPHY sp params``() =
    use cmd = new Address_GetAddressBySpatialLocation()
    cmd.AsyncExecute(SqlGeography.Null) |> ignore


[<Fact>]
let spatialTypes() = 
    use cmd = 
        AdventureWorks.CreateCommand<"
            SELECT OrganizationNode 
            FROM HumanResources.Employee 
            WHERE OrganizationNode = @OrganizationNode
        ", SingleRow = true>()

    let p = SqlHierarchyId.Parse(SqlString("/1/1/"))
    let result = cmd.Execute( p)
    Assert.Equal(Some(Some p), result)
