namespace FSharp.Data.SqlClient

open System
open System.Dynamic
open System.Collections.Generic
open System.Runtime.InteropServices

[<Sealed>]
///<summary>Custom implementation of <see cref='DynamicObject'/></summary>
type RuntimeRecord(data : IDictionary<string,obj>) =
    inherit DynamicObject() 
    do
        assert(data <> null)
    
    override this.ToString() =
        let values = [| 
            for pair in data -> 
                sprintf "%s = %s" pair.Key (if pair.Value = null || Convert.IsDBNull(pair.Value) then "None" else sprintf "%A" pair.Value) |]
        sprintf "{ %s }" <| String.Join("; ", values)
    
    override this.GetDynamicMemberNames() = upcast data.Keys

    override this.TryGetMember( binder, result) = data.TryGetValue(binder.Name, &result)
    
    override this.TrySetMember( binder, result) = 
        let name = binder.Name
        if data.ContainsKey(name) 
        then 
            data.[name] <- result
            true
        else false

    override this.Equals other = 
        match other with
        | :? seq<KeyValuePair<string, obj>> as v -> 
            Linq.Enumerable.SequenceEqual(this, v)
        |_ -> false

    override this.GetHashCode () = data.GetHashCode()

    interface IDictionary<string,obj> with
        member this.Item with get key = data.[key] and set key value = data.[key] <- value
        member this.Keys with get() = data.Keys
        member this.Values with get() = data.Values
        member this.Count with get() = data.Count
        member this.IsReadOnly with get() = true
        member this.ContainsKey(key) = data.ContainsKey(key)
        member this.Add(key, value) = data.Add(key, value)
        member this.Add(value) = data.Add(value)
        member this.Clear() = data.Clear()
        member this.Contains(value) = data.Contains(value)
        member this.CopyTo(arr, index) = data.CopyTo(arr, index)
        member this.Remove(key : string) = data.Remove(key)
        member this.TryGetValue(key, value) = data.TryGetValue(key, &value)
        member this.Remove(key : KeyValuePair<_,_>) = data.Remove(key)
        member this.GetEnumerator() = data.GetEnumerator()
        member this.GetEnumerator() : Collections.IEnumerator = (data :> Collections.IEnumerable).GetEnumerator()
            