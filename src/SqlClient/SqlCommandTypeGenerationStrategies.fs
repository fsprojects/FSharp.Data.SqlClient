namespace FSharp.Data.SqlClient

open Samples.FSharp.ProvidedTypes

type internal BaseStrategy() = 

    member this.GetSqlCommandType(): ProvidedTypeDefinition = 
        Unchecked.defaultof<_> 
