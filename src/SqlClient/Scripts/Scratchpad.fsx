
open System

type Date(dt: DateTime) = 
    member this.Year = dt.Year
    member this.Month = dt.Month
    member this.Day = dt.Day

    static member op_Implicit(x: DateTime) : Date = Date(x)
    static member op_Implicit(x: Date) : DateTime = DateTime(x.Year, x.Month, x.Day)

let now = DateTime.Now

let inline implicit arg =
  ( ^a : (static member op_Implicit : ^b -> ^a) arg)

Convert.ToDateTime(Date(now))

now |> box :> Date

//Convert.ChangeType(Nullable 42, typeof<int>)
Convert.ChangeType(42M, typeof<float>)

