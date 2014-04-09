module FSharp.Data.Tests.RuntimeRecord

open System
open System.Dynamic
open System.Collections.Generic

open Xunit
open FsUnit.Xunit
open Newtonsoft.Json
open FSharp.Data.SqlClient

let recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "null", null; "foo", box "bar"])

[<Fact>]
let ``ToString : Nulls to Nones``() =
    recordWithNulls.ToString() |> should equal @"{ DBNull = None; null = None; foo = ""bar"" }"

type Binder(name) = 
    inherit GetMemberBinder(name, false) 
    override this.FallbackGetMember(_,_) = null

[<Fact>] 
let ``TryGetMember succeeds``() = recordWithNulls.TryGetMember(Binder("DBNull")) |> should equal (true, box DBNull.Value)

[<Fact>] 
let ``TryGetMember fails``() = recordWithNulls.TryGetMember(Binder("foobar")) |> should equal (false, null)

[<Fact>] 
let ``Not equal of different type``() = recordWithNulls.Equals(new obj()) |> should be False

[<Fact>] 
let ``Not equal of different size``() = recordWithNulls = DynamicRecord(dict []) |> should be False

[<Fact>] 
let ``Not equal with different keys``() = recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "bar", null]) |> should be False

[<Fact>] 
let ``Not equal with different values``() = recordWithNulls = DynamicRecord(dict ["DBNull", box DBNull.Value; "foo", box "foo"]) |> should be False

[<Fact>] 
let Equal() = recordWithNulls = DynamicRecord(recordWithNulls) |> should be True

[<Fact>] 
let ``GetHashCode is same when equal``() = 
    let clone = DynamicRecord( dict( recordWithNulls |> Seq.map (fun(KeyValue x) -> x)))
    if recordWithNulls = clone //did if to make assertion more granular
    then 
        recordWithNulls.GetHashCode() = clone.GetHashCode() |> should be True

let dt = DateTime(2012,1,1)
let offset = DateTimeOffset(dt,TimeSpan.FromHours(2.))
let dateRecord = DynamicRecord(dict ["Date", box dt; "Offset", box offset]) 
let dateRecordString = """{"Date":"2012-01-01T00:00:00","Offset":"2012-01-01T00:00:00+02:00"}"""
let recordWithNullsString = """{"DBNull":null,"null":null,"foo":"bar"}"""

[<Fact>] 
let ``JSON serialize``() = 
    JsonConvert.SerializeObject(recordWithNulls) |> should equal recordWithNullsString
    JsonConvert.SerializeObject(dateRecord) |> should equal dateRecordString

//[<Fact>] 
//let ``JSON deserialize``() = 
//    //JsonConvert.DeserializeObject<DynamicRecord>(recordWithNullsString) |> should equal recordWithNulls
//    JsonConvert.DeserializeObject<DynamicRecord>(dateRecordString) |> should equal dateRecord
