namespace FSharp.Data.SqlClient

open System
open System.Dynamic
open System.Collections.Generic
open System.Runtime.InteropServices

type RuntimeRecord(data : IDictionary<string,obj>) =
    inherit DynamicObject() 
    do
        assert(data <> null)
    
    override this.ToString() =
        let values = [| 
            for pair in data -> 
                sprintf "%s = %s" pair.Key (if pair.Value = null || Convert.IsDBNull(pair.Value) then "None" else sprintf "%A" pair.Value) |]
        sprintf "{ %s }" <| String.Join("; ", values)
    
    member this.Count () = data.Count
    member this.Data () = data |> Seq.map(fun p -> p.Key, p.Value)
    member this.Item with get(key) = data.[key]

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
        | :? RuntimeRecord as v -> 
            v.Count() = data.Count && v.Data()
            |> Seq.forall (fun (key,otherValue) -> 
                let found,value = data.TryGetValue(key) 
                found && obj.Equals(value, otherValue))
        |_ -> false

    override this.GetHashCode () = data.GetHashCode()

        
            