
#r @"..\..\..\packages\Newtonsoft.Json.6.0.1\lib\net45\Newtonsoft.Json.dll"
#r @"..\..\..\..\elephant\packages\FSharp.Data.1.1.10\lib\net40\FSharp.Data.dll"

open System
open Newtonsoft.Json

//JsonConvert.SerializeObject([ "key1", 42; "key2", 12 ] |> Seq.map (fun(k, v) -> System.Collections.Generic.KeyValuePair(k, v)))

let DateTimeType() = 
    let utc = DateTime( 2014, 4, 4, 18, 0, 0, DateTimeKind.Utc) //"2014-04-04 18:00:00Z"
    let utcStr = utc.ToString("u")
    let utcLoopback = DateTime.Parse( utcStr)

    printfn "\nDateTime type:\nOriginal value: %A\nOriginal value kind: %O\nstr repr: %s\nLoopback: %A.\nLoopback value kind: %O." utc utc.Kind utcStr utcLoopback utcLoopback.Kind

DateTimeType()

let JSONSerializer() = 
    let utc = DateTime( 2014, 4, 4, 18, 0, 0, DateTimeKind.Utc) //"2014-04-04 18:00:00Z"
    let utcStr = JsonConvert.SerializeObject( utc)
    let utcLoopback = JsonConvert.DeserializeObject<DateTime>( utcStr)

    printfn "\nDateTime type with Json serialization:\nOriginal value: %A\nOriginal value kind: %O\nnSerialized: %s\nLoopback: %A.\nLoopback value kind: %O." utc utc.Kind utcStr utcLoopback utcLoopback.Kind

JSONSerializer()

let DateTimeOffsetType() = 
    let utc = DateTimeOffset( 2014, 4, 4, 18, 0, 0, TimeSpan.Zero) 
    let utcStr = utc.ToString("O")
    let utcLoopback = DateTimeOffset.Parse( utcStr)

    printfn "\nDateTimeOffset type:\nOriginal value: %A\nstr repr: %s\nLoopback: %A." utc utcStr utcLoopback 

DateTimeOffsetType()

let JSONSerializerOffset() = 
    let utc = DateTimeOffset( 2014, 4, 4, 18, 0, 0, TimeSpan.Zero) 
    let utcStr = JsonConvert.SerializeObject( utc)
    let utcLoopback = JsonConvert.DeserializeObject<DateTimeOffset>( utcStr)

    printfn "\nDateTimeOffset type with Json serialization:\nOriginal value: %A\nstr repr: %s\nLoopback: %A." utc utcStr utcLoopback 

JSONSerializerOffset()

open FSharp.Data

type DateTimeParser = JsonProvider<"""{ "startTestUsrTm": "2014-02-25T19:36:28Z"}""">

let DateTimeTypeProvider() = 
    let utc = DateTime( 2014, 4, 4, 18, 0, 0, DateTimeKind.Utc) //"2014-04-04 18:00:00Z"
    let utcStr = JsonConvert.SerializeObject( dict [ "startTestUsrTm", utc])
    //let utcStr = JsonConvert.SerializeObject( dict [ "startTestUsrTm", DateTimeOffset( utc)])
    let utcLoopback = DateTimeParser.Parse( utcStr).StartTestUsrTm

    printfn "\nJSONType Provider with DateTime type:\nOriginal value: %A\nOriginal value kind: %O\nstr repr: %s\nLoopback: %A.\nLoopback value kind: %O." utc utc.Kind utcStr utcLoopback utcLoopback.Kind

DateTimeTypeProvider()
