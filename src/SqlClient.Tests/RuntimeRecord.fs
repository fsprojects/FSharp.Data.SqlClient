module FSharp.Data.Tests.RuntimeRecord

open FSharp.Data.SqlClient
open System
open System.Dynamic
open Xunit
open FsUnit.Xunit

let recordWithNulls = RuntimeRecord(dict ["DBNull", box DBNull.Value; "null", null; "foo", box "bar"])

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
let ``Not equal of different size``() = recordWithNulls = RuntimeRecord(dict []) |> should be False

[<Fact>] 
let ``Not equal with different keys``() = recordWithNulls = RuntimeRecord(dict ["DBNull", box DBNull.Value; "bar", null]) |> should be False

[<Fact>] 
let ``Not equal with different values``() = recordWithNulls = RuntimeRecord(dict ["DBNull", box DBNull.Value; "foo", box "foo"]) |> should be False

[<Fact>] 
let Equal() = recordWithNulls = RuntimeRecord(recordWithNulls.Data() |> dict) |> should be True
