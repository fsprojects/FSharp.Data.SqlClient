namespace FSharp.Data.SqlClient

open System
open System.Dynamic
open System.Collections
open System.Collections.Generic

[<Sealed>]
type StoredProcedureResult<'TItem>(resultSet: seq<'TItem>, returnValue: int, outputParameters: Map<string, obj>) = 
    member this.ReturnValue = returnValue
    member this.Item with get key = outputParameters.[key] 

    interface IEnumerable<'TItem> with
        member __.GetEnumerator() = resultSet.GetEnumerator()
        member __.GetEnumerator() = (resultSet :> IEnumerable).GetEnumerator()
    
        
            