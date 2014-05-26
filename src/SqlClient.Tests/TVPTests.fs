module FSharp.Data.Tests.TVP

open FSharp.Data
open System.Data
open Xunit

// If compile fails here, check prereqs.sql
type TableValuedTuple = SqlCommandProvider<"exec myProc @x", "name=AdventureWorks2012", SingleRow = true, ResultType = ResultType.Tuples>
type MyTableType = TableValuedTuple.MyTableType

[<Fact>]
let Basic() = 
    let cmd = new TableValuedTuple()
    let p = [
        MyTableType(myId = 1, myName = "monkey")
        MyTableType(myId = 2, myName = "donkey")
    ]
    Assert.Equal(Some(1, Some "monkey"), cmd.Execute(x = p))    

[<Fact>] 
let InputIsEnumeratedExactlyOnce() = 
    let cmd = new TableValuedTuple()
    let counter = ref 0
    let x = seq { 
         counter := !counter + 1
         yield MyTableType(myId = 1)
         yield MyTableType(myId = 2, myName = "donkey")
    }
    cmd.Execute x |> ignore
    Assert.Equal(1, !counter)    

[<Fact>] 
let NullableColumn() = 
    let cmd = new TableValuedTuple()
    let p = [
        MyTableType(myId = 1)
        MyTableType(myId = 2, myName = "donkey")
    ]
    Assert.Equal(Some(1, None), cmd.Execute p)    


type TableValuedSingle = SqlCommandProvider<"exec SingleElementProc @x", ConnectionStringOrName = "name=AdventureWorks2012">

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

type TableValuedSprocTuple  = SqlCommandProvider<"exec myProc @x", ConnectionStringOrName = "name=AdventureWorks2012", SingleRow = true, ResultType = ResultType.Tuples>

[<Fact>]
let SprocTupleValue() = 
    let cmd = new TableValuedSprocTuple()
    let p = [
        TableValuedSprocTuple.MyTableType(myId = 1, myName = "monkey")
        TableValuedSprocTuple.MyTableType(myId = 2, myName = "donkey")
    ]
    let actual = cmd.Execute(p).Value
    Assert.Equal((1, Some "monkey"), actual)    


