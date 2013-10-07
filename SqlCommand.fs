[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Data.SqlClient.SqlCommand

open System.Data
open System.Data.SqlClient

type SqlCommand with
    member this.AsyncExecuteReader(behavior : CommandBehavior) =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) |> Async.Ignore 

