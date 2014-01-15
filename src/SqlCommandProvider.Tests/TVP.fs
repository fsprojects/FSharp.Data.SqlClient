module FSharp.Data.Experimental.Tests.TVP

open FSharp.Data.Experimental
open System.Data
open Xunit

// If compile fails here, check prereqs.sql
type TableValuedTuple = SqlCommand<"exec myProc @x", ConnectionStringName = "AdventureWorks2012", SingleRow = true>
type MyTableType = TableValuedTuple.MyTableType

[<Fact>]
let tableValuedTupleValue() = 
    let cmd = new TableValuedTuple()
    let x = TableValuedTuple.MyTableType(myId = 5, myName = "test")
    let p = [
        MyTableType(myId = 1, myName = "monkey")
        MyTableType(myId = 2, myName = "donkey")
    ]
    Assert.Equal((1, Some "monkey"), cmd.Execute(x = p))    

[<Fact>] 
let tvpInputIsEnumeratedExactlyOnce() = 
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
let tableValuedSprocTupleNull() = 
    let cmd = new TableValuedTuple()
    let p = [
        MyTableType(myId = 1)
        MyTableType(myId = 2, myName = "donkey")
    ]
    Assert.Equal((1, None), cmd.Execute p)    


type TableValuedSingle = SqlCommand<"exec SingleElementProc @x", ConnectionStringName = "AdventureWorks2012">

[<Fact>]
let tableValuedSingle() = 
    let cmd = new TableValuedSingle()
    let p = [ 
        TableValuedSingle.SingleElementType(myId = 1) 
        TableValuedSingle.SingleElementType(myId = 2) 
    ]
    let result = cmd.Execute(x = p) |> List.ofSeq
    Assert.Equal<int list>([1;2], result)    

type TableValuedSprocTuple  = SqlCommand<"myProc", ConnectionStringName = "AdventureWorks2012", SingleRow = true, CommandType = CommandType.StoredProcedure>

[<Fact>]
let tableValuedSprocTupleValue() = 
    let cmd = new TableValuedSprocTuple()
    let p = [
        TableValuedSprocTuple.MyTableType(myId = 1, myName = "monkey")
        TableValuedSprocTuple.MyTableType(myId = 2, myName = "donkey")
    ]
    let actual = cmd.Execute(p1 = p)
    Assert.Equal((1, Some "monkey"), actual)    


