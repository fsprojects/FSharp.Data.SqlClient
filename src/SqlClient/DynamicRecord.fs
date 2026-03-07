namespace FSharp.Data.SqlClient

open System
open System.Dynamic
open System.Collections.Generic

[<Sealed>]
///<summary>Custom implementation of <see cref='DynamicObject'/></summary>
type DynamicRecord(data: IDictionary<string, obj>) =
    inherit DynamicObject()

    do assert (not (isNull data))

    member internal this.Data = data

    //Erase to this
    member this.Item
        with get key = data.[key]

    //Dynamic support for JSON.NET serialization
    override this.GetDynamicMemberNames() = upcast data.Keys

    override this.TryGetMember(binder, result) = data.TryGetValue(binder.Name, &result)

    override this.TrySetMember(binder, result) =
        let name = binder.Name

        if data.ContainsKey(name) then
            data.[name] <- result
            true
        else
            false

    //Object overrides
    override this.Equals other =
        match other with
        | :? DynamicRecord as v -> Linq.Enumerable.SequenceEqual(data, v.Data)
        | _ -> false

    override this.GetHashCode() = List.ofSeq(data).GetHashCode()

    override this.ToString() =
        [| for KeyValue(key, value) in data ->
               sprintf
                   "%s = %s"
                   key
                   (if isNull value || Convert.IsDBNull(value) then
                        "None"
                    else
                        sprintf "%A" value) |]
        |> String.concat "; "
        |> sprintf "{ %s }"
