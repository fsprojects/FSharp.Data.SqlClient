namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient

open FSharp.Data.SqlClient

type ISqlCommand = 
    abstract Execute: parameters: (string * obj)[] -> obj
    abstract AsyncExecute: parameters: (string * obj)[] -> obj
    abstract ToTraceString: parameters: (string * obj)[] -> string
    abstract Raw: SqlCommand with get

type RowMapping = obj[] -> obj

module Seq = 

    let internal toOption source =  
        match source |> Seq.truncate 2 |> Seq.toArray with
        | [||] -> None
        | [| x |] -> Some x
        | _ -> invalidArg "source" "The input sequence contains more than one element."

    let internal ofReader<'TItem> rowMapping (reader : SqlDataReader) = 
        seq {
            use __ = reader
            while reader.Read() do
                let values = Array.zeroCreate reader.FieldCount
                reader.GetValues(values) |> ignore
                yield values |> rowMapping |> unbox<'TItem>
        }

[<RequireQualifiedAccess>]
type ResultRank = 
    | Sequence = 0
    | SingleRow = 1
    | ScalarValue = 2

module SqlCommand = 

    let setParameters (cmd: SqlCommand) (parameters: (string * obj)[]) = 
        for name, value in parameters do
            
            let p = cmd.Parameters.[name]            

            if value = null 
            then 
                p.Value <- DBNull.Value 
            else
                if not( p.SqlDbType = SqlDbType.Structured)
                then 
                    p.Value <- value
                else
                    let table : DataTable = unbox p.Value
                    table.Rows.Clear()
                    for rowValues in unbox<seq<obj[]>> value do
                        table.Rows.Add( rowValues) |> ignore

            if Convert.IsDBNull p.Value 
            then 
                match p.SqlDbType with
                | SqlDbType.NVarChar -> p.Size <- 4000
                | SqlDbType.VarChar -> p.Size <- 8000
                | _ -> ()

    let executeReader cmd getReaderBehavior parameters = 
        setParameters cmd parameters      
        cmd.ExecuteReader( getReaderBehavior())

    let asyncExecuteReader cmd getReaderBehavior parameters = 
        setParameters cmd parameters 
        cmd.AsyncExecuteReader( getReaderBehavior())
    
    let executeDataTable cmd getReaderBehavior parameters = 
        use reader = executeReader cmd getReaderBehavior parameters 
        let result = new FSharp.Data.DataTable<DataRow>()
        result.Load(reader)
        result

    let asyncExecuteDataTable cmd getReaderBehavior parameters = 
        async {
            use! reader = asyncExecuteReader cmd getReaderBehavior parameters 
            let result = new FSharp.Data.DataTable<DataRow>()
            result.Load(reader)
            return result
        }

    let executeSeq<'TItem> cmd rowMapper getReaderBehavior rank parameters = 
        let xs = parameters |> executeReader cmd getReaderBehavior |> Seq.ofReader<'TItem> rowMapper

        if rank = ResultRank.SingleRow 
        then 
            xs |> Seq.toOption |> box
        elif rank = ResultRank.ScalarValue 
        then 
            xs |> Seq.exactlyOne |> box
        else 
            assert (rank = ResultRank.Sequence)
            box xs 
            
    let asyncExecuteSeq<'TItem> cmd rowMapper getReaderBehavior rank parameters = 
        let xs = 
            async {
                let! reader = asyncExecuteReader cmd getReaderBehavior parameters 
                return reader |> Seq.ofReader<'TItem> rowMapper
            }

        if rank = ResultRank.SingleRow
        then
            async {
                let! xs = xs 
                return xs |> Seq.toOption
            }
            |> box
        elif rank = ResultRank.ScalarValue 
        then 
            async {
                let! xs = xs 
                return xs |> Seq.exactlyOne
            }
            |> box       
        else 
            assert (rank = ResultRank.Sequence)
            box xs 

    let executeNonQuery cmd parameters = 
        setParameters cmd parameters  
        use openedConnection = cmd.Connection.UseLocally()
        cmd.ExecuteNonQuery() 

    let asyncExecuteNonQuery cmd parameters = 
        setParameters cmd parameters  
        async {         
            use openedConnection = cmd.Connection.UseLocally()
            return! cmd.AsyncExecuteNonQuery() 
        }

type Connection =
    | Literal of string
    | NameInConfig of string
    | Transaction of SqlTransaction

type SqlCommand<'TItem> (connection, sqlStatement, parameters, resultType, rank: ResultRank, rowMapping: RowMapping, isStoredProcedure) = 

    let cmd = new SqlCommand(sqlStatement, CommandType = if isStoredProcedure then CommandType.StoredProcedure else CommandType.Text)
    do 
        match connection with
        | Literal x -> 
            cmd.Connection <- new SqlConnection(x)
        | NameInConfig x ->
            let connStr = Configuration.GetConnectionStringRunTimeByName x
            cmd.Connection <- new SqlConnection(connStr)
        | Transaction t ->
             cmd.Connection <- t.Connection
             cmd.Transaction <- t

    do
        cmd.Parameters.AddRange( parameters)

    let getReaderBehavior() = 
        seq {
            yield CommandBehavior.SingleResult

            if cmd.Connection.State <> ConnectionState.Open 
            then
                cmd.Connection.Open() 
                yield CommandBehavior.CloseConnection

            if rank = ResultRank.SingleRow then yield CommandBehavior.SingleRow 

            if resultType = ResultType.DataTable then yield CommandBehavior.KeyInfo
        }
        |> Seq.reduce (|||) 

    let execute, asyncExecute = 
        match resultType with
        | ResultType.Records | ResultType.Tuples ->
            if box rowMapping = null
            then
                SqlCommand.executeNonQuery cmd >> box, SqlCommand.asyncExecuteNonQuery cmd >> box
            else
                SqlCommand.executeSeq<'TItem> cmd rowMapping getReaderBehavior rank >> box, 
                SqlCommand.asyncExecuteSeq<'TItem> cmd rowMapping getReaderBehavior rank >> box
        | ResultType.DataTable ->
            SqlCommand.executeDataTable cmd getReaderBehavior >> box, SqlCommand.asyncExecuteDataTable cmd getReaderBehavior >> box
        | ResultType.DataReader -> 
            SqlCommand.executeReader cmd getReaderBehavior >> box, SqlCommand.asyncExecuteReader cmd getReaderBehavior >> box
        | unexpected -> failwithf "Unexpected ResultType value: %O" unexpected

    member this.AsSqlCommand () = 
        let clone = new SqlCommand(cmd.CommandText, new SqlConnection(cmd.Connection.ConnectionString), CommandType = cmd.CommandType)
        clone.Parameters.AddRange <| [| for p in cmd.Parameters -> SqlParameter(p.ParameterName, p.SqlDbType) |]
        clone

    interface ISqlCommand with

        member this.Execute parameters = execute parameters
        member this.AsyncExecute parameters = asyncExecute parameters

        member this.ToTraceString parameters =  
            let clone = this.AsSqlCommand()
            SqlCommand.setParameters clone parameters  
            let parameterDefinition (p : SqlParameter) =
                if p.Size <> 0 then
                    sprintf "%s %A(%d)" p.ParameterName p.SqlDbType p.Size
                else
                    sprintf "%s %A" p.ParameterName p.SqlDbType 
            seq {
              yield sprintf "exec sp_executesql N'%s'" clone.CommandText
              
              yield clone.Parameters
                    |> Seq.cast<SqlParameter> 
                    |> Seq.map parameterDefinition
                    |> String.concat ","
                    |> sprintf "N'%s'" 
              yield parameters
                    |> Seq.map(fun (name,value) -> sprintf "%s='%O'" name value) 
                    |> String.concat ","
            } |> String.concat "," //Using string.concat to handle annoying case with no parameters

        member this.Raw = cmd
            
    interface IDisposable with
        member this.Dispose() =
            cmd.Dispose()

