module FSharp.Data.Tests.DynamicRecordTests

open System
open System.Dynamic
open System.Collections.Generic

open Xunit
open Newtonsoft.Json
open FSharp.Data.SqlClient

let recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "null", null; "foo", box "bar"])

[<Fact>]
let ``ToString : Nulls to Nones``() =
    Assert.Equal<string>(
        expected = @"{ DBNull = None; null = None; foo = ""bar"" }",
        actual = recordWithNulls.ToString()
    )

type Binder(name) = 
    inherit GetMemberBinder(name, false) 
    override this.FallbackGetMember(_,_) = null

[<Fact>] 
let ``TryGetMember succeeds``() = 
    Assert.Equal(
        expected = (true, box DBNull.Value),
        actual = recordWithNulls.TryGetMember(Binder("DBNull"))
    )

[<Fact>] 
let ``TryGetMember fails``() = 
    Assert.Equal(
        expected = recordWithNulls.TryGetMember(Binder("foobar")),
        actual = (false, null)
    )

[<Fact>] 
let ``Not equal of different type``() = 
    Assert.False( recordWithNulls.Equals( obj()))

[<Fact>] 
let ``Not equal of different size``() = 
    let condition = recordWithNulls = DynamicRecord(dict [])
    Assert.False condition

[<Fact>] 
let ``Not equal with different keys``() = 
    let condition = recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "bar", null])
    Assert.False condition

[<Fact>] 
let ``Not equal with different values``() = 
    let condition = recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "foo", box "foo"])
    Assert.False condition

let getData(x: DynamicRecord) = 
    dict [ for name in x.GetDynamicMemberNames() -> name, x.[name] ]

[<Fact>] 
let Equal() = 
    let data = getData recordWithNulls
    let condition = recordWithNulls = DynamicRecord(data)
    Assert.True condition

[<Fact>] 
let ``GetHashCode is same when equal``() = 
    let data = getData recordWithNulls
    let clone = DynamicRecord( data)
    if recordWithNulls = clone //did if to make assertion more granular
    then 
        let condition = recordWithNulls.GetHashCode() = clone.GetHashCode() 
        Assert.True condition

let dt = DateTime(2012,1,1)
let offset = DateTimeOffset(dt,TimeSpan.FromHours(2.))
let data = dict ["Date", box dt; "Offset", box offset]
let dateRecord = DynamicRecord(data) 
let dateRecordString = """{"Date":"2012-01-01T00:00:00","Offset":"2012-01-01T00:00:00+02:00"}"""
let recordWithNullsString = """{"DBNull":null,"null":null,"foo":"bar"}"""

[<Fact>] 
let ``JSON serialize``() = 
    Assert.Equal<string>(
        expected = recordWithNullsString,
        actual = JsonConvert.SerializeObject recordWithNulls
    )

    Assert.Equal<string>(
        expected = dateRecordString,
        actual = JsonConvert.SerializeObject( dateRecord)
    )

[<Fact>] 
let ToString() = 
    Assert.Equal<string>(
        expected = sprintf "{ Date = %A; Offset = %A }" data.["Date"] data.["Offset"],
        actual = dateRecord.ToString()
    )



