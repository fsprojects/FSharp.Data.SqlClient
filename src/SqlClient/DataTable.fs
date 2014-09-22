namespace FSharp.Data

open System.Data
open System.Collections.Generic

[<Sealed>]
///<summary>Generic implementation of <see cref='DataTable'/></summary>
type DataTable<'T when 'T :> DataRow>() = 
    inherit DataTable() 

    interface IList<'T> with
        member this.GetEnumerator() = this.Rows.GetEnumerator()
        member this.GetEnumerator() : IEnumerator<'T> = (Seq.cast<'T> this.Rows).GetEnumerator() 

        member this.Count = this.Rows.Count
        member this.IsReadOnly = this.Rows.IsReadOnly
        member this.Item 
            with get index = downcast this.Rows.[index]
            and set index row = 
                this.Rows.RemoveAt(index)
                this.Rows.InsertAt(row, index)

        member this.Add row = this.Rows.Add row
        member this.Clear() = this.Rows.Clear()
        member this.Contains row = this.Rows.Contains row
        member this.CopyTo(dest, index) = this.Rows.CopyTo(dest, index)
        member this.IndexOf row = this.Rows.IndexOf row
        member this.Insert(index, row) = this.Rows.InsertAt(row, index)
        member this.Remove row = this.Rows.Remove(row); true
        member this.RemoveAt index = this.Rows.RemoveAt(index)

