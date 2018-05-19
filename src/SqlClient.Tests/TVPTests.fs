module FSharp.Data.TVPTests

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

open System.Data.SqlClient

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
