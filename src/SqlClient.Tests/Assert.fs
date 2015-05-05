[<AutoOpen>]
module AssertExtensions

open Xunit

type Assert with 
    static member IsNone (value: _ option) = 
        Assert.True value.IsNone
