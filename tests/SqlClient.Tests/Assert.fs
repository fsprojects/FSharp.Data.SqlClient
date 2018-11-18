[<AutoOpen>]
module AssertExtensions

open Xunit

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do()

type Assert with 
    static member IsNone (value: _ option) = 
        Assert.True value.IsNone
