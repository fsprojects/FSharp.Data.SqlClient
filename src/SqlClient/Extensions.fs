namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient

[<AutoOpen>]
module Extensions =

    type SqlDataReader with
        member internal this.MapRowValues<'TItem>( rowMapping) = 
            seq {
                use _ = this
                let values = Array.zeroCreate this.FieldCount
                while this.Read() do
                    this.GetValues(values) |> ignore
                    yield values |> rowMapping |> unbox<'TItem>
            }

    type SqlDataReader with
        member internal this.TryGetValue(name: string) = 
            let value = this.[name] 
            if Convert.IsDBNull value then None else Some(unbox<'a> value)
        member internal this.GetValueOrDefault<'a>(name: string, defaultValue) = 
            let value = this.[name] 
            if Convert.IsDBNull value then defaultValue else unbox<'a> value

    type SqlCommand with
        member this.AsyncExecuteReader (behavior:CommandBehavior) = 
            #if NET40
            Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)            
            #else
            Async.AwaitTask(this.ExecuteReaderAsync(behavior))
            #endif

        member this.AsyncExecuteNonQuery() =
            #if NET40
            Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery)
            #else
            Async.AwaitTask(this.ExecuteNonQueryAsync())            
            #endif

        static member internal DefaultTimeout = (new SqlCommand()).CommandTimeout

        member internal this.ExecuteQuery mapper = 
            seq {
                use cursor = this.ExecuteReader()
                while cursor.Read() do
                    yield mapper cursor
            }

    type SqlConnection with

     //address an issue when regular Dispose on SqlConnection needed for async computation 
     //wipes out all properties like ConnectionString in addition to closing connection to db
        member this.UseLocally(?privateConnection) =
            if this.State = ConnectionState.Closed 
                && defaultArg privateConnection true
            then 
                this.Open()
                { new IDisposable with member __.Dispose() = this.Close() }
            else { new IDisposable with member __.Dispose() = () }
        
        member this.IsSqlAzure = 
            assert (this.State = ConnectionState.Open)
            use cmd = new SqlCommand("SELECT SERVERPROPERTY('edition')", this)
            cmd.ExecuteScalar().Equals("SQL Azure")
