module FSharp.Data.``The undeclared parameter 'X' is used more than once in the batch being analyzed``

open System
open Xunit

[<Fact>]
let Basic() =
    use cmd = new SqlCommandProvider<"
        SELECT * 
        FROM HumanResources.Shift 
        WHERE 
            @time >= StartTime 
            AND @time <= EndTime
    ", ConnectionStrings.AdventureWorksNamed>()
    let actual = [ for x in cmd.Execute( TimeSpan(16, 0, 0)) -> x.Name ]
    Assert.Equal<_ list>([ "Evening" ], actual )

[<Fact>]
let WithBoundDeclaration() =
    use cmd = new SqlCommandProvider<"
        DECLARE @x AS INT = 42; --make bound vars handled properly

        SELECT * 
        FROM HumanResources.Shift 
        WHERE 
            @time >= StartTime 
            AND @time <= EndTime
    ", ConnectionStrings.AdventureWorksNamed>()
    let actual = [ for x in cmd.Execute( TimeSpan(16, 0, 0)) -> x.Name ]
    Assert.Equal<_ list>([ "Evening" ], actual )

[<Fact>]
let WithUnboundDeclaration() =
    use cmd = new SqlCommandProvider<"
        DECLARE @x AS INT; --make bound vars handled properly
        SELECT * 
        FROM HumanResources.Shift 
        WHERE 
            @time >= StartTime 
            AND @time <= EndTime
    ", ConnectionStrings.AdventureWorksNamed>()
    let actual = [ for x in cmd.Execute( TimeSpan(16, 0, 0)) -> x.Name ]
    Assert.Equal<_ list>([ "Evening" ], actual )

[<Fact>]
let DynamicFiltering() =
    use cmd = new SqlCommandProvider<"
        SELECT * 
        FROM HumanResources.Shift 
        WHERE CAST(@time AS TIME) IS NULL OR @time BETWEEN StartTime AND EndTime
    ", ConnectionStrings.AdventureWorksNamed>()
    let actual = [ for x in cmd.Execute( TimeSpan(16, 0, 0)) -> x.Name ]
    Assert.Equal<_ list>([ "Evening" ], actual )


