namespace FSharp.Data.SqlProgrammability

open System.Data
open System.Collections.Generic

[<Sealed>]
type DataTable<'T when 'T :> DataRow>() = 
    inherit DataTable() 

    member this.Item index : 'T = downcast this.Rows.[index] 

    interface ICollection<'T> with
        member this.GetEnumerator() = this.Rows.GetEnumerator()
        member this.GetEnumerator() : IEnumerator<'T> = (Seq.cast<'T> this.Rows).GetEnumerator() 
        member this.Count = this.Rows.Count
        member this.IsReadOnly = this.Rows.IsReadOnly
        member this.Add row = this.Rows.Add row
        member this.Clear() = this.Rows.Clear()
        member this.Contains row = this.Rows.Contains row
        member this.CopyTo(dest, index) = this.Rows.CopyTo(dest, index)
        member this.Remove row = this.Rows.Remove(row); true

//    later
//    interface IReadOnlyList<DataRow> with
//        member this.Item with get index = this.Rows.[index]


