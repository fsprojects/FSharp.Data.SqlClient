﻿#if WITH_LEGACY_NAMESPACE
module FSharp.Data.TVPTests
open FSharp.Data.SqlClient
#else
module FSharp.Data.SqlClient.TVPTests
#endif

open FSharp.Data
open Xunit

// If compile fails here, check prereqs.sql
type TableValuedTuple = SqlCommandProvider<"exec Person.myProc @x", ConnectionStrings.AdventureWorksNamed, SingleRow = true, ResultType = ResultType.Tuples>
type MyTableType = TableValuedTuple.MyTableType

[<Fact>]
let Basic() = 
    let cmd = new TableValuedTuple()
    let p = [
        MyTableType(myId = 1, myName = Some "monkey")
        MyTableType(myId = 2, myName = Some "donkey")
    ] 
    Assert.Equal(Some(1, Some "monkey"), cmd.Execute(x = p))    

[<Fact(Skip = "Flucky")>] 
let InputIsEnumeratedExactlyOnce() = 
    let cmd = new TableValuedTuple()
    let counter = ref 0
    let x = seq { 
         counter := !counter + 1
         yield MyTableType(myId = 1)
         yield MyTableType(myId = 2, myName = Some "donkey")
    }
    cmd.Execute x |> ignore
    Assert.Equal(1, !counter)    

[<Fact>] 
let NullableColumn() = 
    let cmd = new TableValuedTuple()
    let p = [
        MyTableType(myId = 1)
        MyTableType(myId = 2, myName = Some "donkey")
    ]
    Assert.Equal(Some(1, None), cmd.Execute p)    

type TableValuedSingle = SqlCommandProvider<"exec SingleElementProc @x", ConnectionStringOrName = ConnectionStrings.AdventureWorksNamed>

[<Fact>]
let SingleColumn() = 
    let cmd = new TableValuedSingle()
    let p = [ 
        TableValuedSingle.SingleElementType(myId = 1) 
        TableValuedSingle.SingleElementType(myId = 2) 
    ]
    let result = cmd.Execute(x = p) |> List.ofSeq
    Assert.Equal<int list>([1;2], result)    

[<Fact>]
let tvpSqlParamCleanUp() = 
    let cmd = new TableValuedSingle()
    let p = [ 
        TableValuedSingle.SingleElementType(myId = 1) 
        TableValuedSingle.SingleElementType(myId = 2) 
    ]
    cmd.Execute(x = p) |> List.ofSeq |> ignore
    let result = cmd.Execute(x = p) |> List.ofSeq
    Assert.Equal<int list>([1;2], result)    

type TableValuedSprocTuple  = SqlCommandProvider<"exec Person.myProc @x", ConnectionStringOrName = ConnectionStrings.AdventureWorksNamed, SingleRow = true, ResultType = ResultType.Tuples>

[<Fact>]
let SprocTupleValue() = 
    let cmd = new TableValuedSprocTuple()
    let p = [
        TableValuedSprocTuple.MyTableType(myId = 1, myName = Some "monkey")
        TableValuedSprocTuple.MyTableType(myId = 2, myName = Some "donkey")
    ]
    let actual = cmd.Execute(p).Value
    Assert.Equal((1, Some "monkey"), actual)    

[<Fact>]
let ``SprocTupleValue works with empty table``() = 
    let cmd = new TableValuedSprocTuple()
    let p = []
    let actual = cmd.Execute(p)
    Assert.Equal(None, actual)    

type TableValuedTupleWithOptionalParams = SqlCommandProvider<"exec Person.myProc @x", ConnectionStrings.AdventureWorksNamed, AllParametersOptional = true>
[<Fact>]
let TableValuedTupleWithOptionalParams() = 
    let cmd = new TableValuedTupleWithOptionalParams()
    cmd.Execute Array.empty |> ignore
(*
    don't delete this test. The previous line fails with if combo of TVP and AllParametersOptional = true is not handled properly
Error	1	The type provider 'FSharp.Data.SqlCommandProvider' reported an error in the context of provided type 'FSharp.Data.SqlCommandProvider,CommandText="exec myProc @x",ConnectionStringOrName="name=AdventureWorks2012",AllParametersOptional="True"', member 'Execute'. 
The error: Value cannot be null.	C:\Users\mitekm\Documents\GitHub\FSharp.Data.SqlClient\src\SqlClient.Tests\TVPTests.fs	83	5	SqlClient.Tests
*)
   

type MyFunc = SqlCommandProvider<"select * from dbo.MyFunc(@x, @y)", ConnectionStrings.AdventureWorksNamed>

[<Fact>]
let TwoTVPParameterOfSameUDTT() = 
    let cmd = new MyFunc()
    let xs = [ 1, Some "monkey" ]
    let ys = [ 2, Some "donkey" ]
    let xs' = [ for id, name in xs -> MyFunc.MyTableType(id, name) ]
    let ys' = [ for id, name in ys -> MyFunc.MyTableType(id, name) ]
    let expected = [ for id, name in xs @ ys -> MyFunc.Record(id, name) ]
    Assert.Equal<_ list>(expected, cmd.Execute(xs', ys') |> Seq.toList)    

open Microsoft.Data.SqlClient

[<Fact>]
let ReuseTVPTypeForDynamicADONET() = 
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    use cmd = new SqlCommand("exec Person.myProc @x", conn)
    let p = cmd.Parameters.Add( "@x", System.Data.SqlDbType.Structured)
    p.TypeName <- "Person.MyTableType"
    p.Value <- [
        MyTableType(myId = 1, myName = Some "monkey")
        MyTableType(myId = 2, myName = Some "donkey")
    ] |> Seq.cast<Microsoft.SqlServer.Server.SqlDataRecord>
    conn.Open()
    let expected = [ 1, "monkey"; 2, "donkey" ]
    let actual = [
        use cursor = cmd.ExecuteReader()
        while cursor.Read() do
            yield cursor.GetInt32(0), cursor.GetString(1)
    ]

    Assert.Equal<_ list>( expected, actual)

type QueryTVO = 
    SqlCommandProvider<"
        DECLARE @p1 AS dbo.MyTableType = @input
        SELECT * from @p1
    ", ConnectionStrings.AdventureWorksNamed>

[<Fact(Skip ="Fails at runtime :(")>]
let UsingTVPInQuery() = 
    use cmd = new QueryTVO()
    let expected = [ 
        1, Some "monkey"
        2, Some "donkey"
    ]

    let actual =
        cmd.Execute(input = [ for id, name in expected -> QueryTVO.MyTableType(id, name) ])
        |> Seq.map(fun x -> x.myId, x.myName)
        |> Seq.toList

    Assert.Equal<_ list>(expected, actual)

type MappedTVP = 
    SqlCommandProvider<"
        SELECT myId, myName from @input
    ", ConnectionStrings.AdventureWorksLiteral, TableVarMapping = "@input=dbo.MyTableType">
[<Fact>]
let UsingMappedTVPInQuery() = 
    printfn "%s" ConnectionStrings.AdventureWorksLiteral
    use cmd = new MappedTVP(ConnectionStrings.AdventureWorksLiteral)
    let expected = [ 
        1, Some "monkey"
        2, Some "donkey"
    ]

    let actual =
        cmd.Execute(input = [ for id, name in expected -> MappedTVP.MyTableType(id, name) ])
        |> Seq.map(fun x -> x.myId, x.myName)
        |> Seq.toList

    Assert.Equal<_ list>(expected, actual)

type MappedTVPFixed = 
    SqlCommandProvider<"
        SELECT myId, myName from @input
    ", ConnectionStrings.AdventureWorksLiteral, TableVarMapping = "@input=dbo.MyTableTypeFixed">
[<Fact>]
let UsingMappedTVPFixedInQuery() = 
    printfn "%s" ConnectionStrings.AdventureWorksLiteral
    use cmd = new MappedTVPFixed(ConnectionStrings.AdventureWorksLiteral)
    let expected = [ 
        1, Some "monkey"
        2, Some "donkey"
    ]

    let actual =
        cmd.Execute(input = [ for id, name in expected -> MappedTVPFixed.MyTableTypeFixed(id, name) ])
        |> Seq.map(fun x -> x.myId, x.myName |> Option.map (fun s -> s.Trim()))
        |> Seq.toList

    Assert.Equal<_ list>(expected, actual)

type AdventureWorks = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksNamed>
[<Fact>]
let ``issue #345 decimal in TVP gets rounded`` () =
    let value = Some 1.2345M
    let tvp = [AdventureWorks.dbo.``User-Defined Table Types``.decimal_test_tvp(value)]
    use cmd = new AdventureWorks.dbo.decimal_test(ConnectionStrings.AdventureWorksLiteral)
    let resultvalue = cmd.Execute(tvp) |> Seq.head
    Assert.Equal(value, resultvalue)

[<Fact>]
let ``issue #393 troubleshoot if datetimeoffset raises an exception`` () =
    // N.B, this should be tested against SQL Azure
    let value = System.DateTimeOffset.UtcNow
    let tvp = [AdventureWorks.dbo.``User-Defined Table Types``.datetimeoffset_test_tvp(value)]
    use cmd = new AdventureWorks.dbo.datetimeoffset_test(ConnectionStrings.AdventureWorksLiteral)
    let resultvalue = cmd.Execute(tvp) |> Seq.head
    Assert.Equal(value, resultvalue)

type FixedLengthBinaryTVP = SqlCommandProvider<"EXEC [dbo].[FixedLengthBinaryTVPTestProc] @fixedLengthBinaryTests", ConnectionStrings.AdventureWorksLiteral>
[<Fact>]
let ``Using Fixed Length Binary TVP``() =
    printfn "%s" ConnectionStrings.AdventureWorksLiteral
    use cmd = new FixedLengthBinaryTVP(ConnectionStrings.AdventureWorksLiteral)

    [
        [|1uy;2uy;3uy|]
        [|4uy;5uy;6uy|]
        [|7uy;8uy;9uy|]
    ]
    |> Seq.map (fun d -> FixedLengthBinaryTVP.FixedLengthBinaryTVPTest (Some d))
    |> cmd.Execute
    |> ignore


type TestTVPColumnOrder = SqlCommandProvider<"EXEC [dbo].[TestTVPColumnOrder] @tvp", ConnectionStrings.AdventureWorksLiteral>
[<Fact>]
let ``User Defined Table Types should list columns orderd by Column Id (i.e. the order in which they appear in the declared type)`` () =
    use cmd = new TestTVPColumnOrder(ConnectionStrings.AdventureWorksLiteral)

    [
        (1, "some string",        true)
        (2, "some other string",  false)
        (3, "yet another string", true)
    ]
    |> Seq.map (fun (i, s, b) -> TestTVPColumnOrder.TVPColumnOrder(i, s, b))
    |> Seq.toList
    |> cmd.Execute
    |> ignore