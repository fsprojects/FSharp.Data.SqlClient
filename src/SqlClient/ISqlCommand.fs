namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient
open System.Reflection

open FSharp.Data.SqlClient

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type ISqlCommand = 
    
    abstract Execute: parameters: (string * obj)[] -> obj
    abstract AsyncExecute: parameters: (string * obj)[] -> obj
    abstract ExecuteSingle: parameters: (string * obj)[] -> obj
    abstract AsyncExecuteSingle: parameters: (string * obj)[] -> obj

    abstract ToTraceString: parameters: (string * obj)[] -> string

    abstract Raw: SqlCommand with get

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type RowMapping = obj[] -> obj

module Seq = 

    let internal toOption source =  
        match source |> Seq.truncate 2 |> Seq.toArray with
        | [||] -> None
        | [| x |] -> Some x
        | _ -> invalidArg "source" "The input sequence contains more than one element."

    let internal ofReader<'TItem> rowMapping (reader : SqlDataReader) = 
        seq {
            use _ = reader
            while reader.Read() do
                let values = Array.zeroCreate reader.FieldCount
                reader.GetValues(values) |> ignore
                yield values |> rowMapping |> unbox<'TItem>
        }

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
[<RequireQualifiedAccess>]
type ResultRank = 
    | Sequence = 0
    | SingleRow = 1
    | ScalarValue = 2

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type Connection =
    | Literal of string
    | NameInConfig of string
    | ``Connection and-or Transaction`` of SqlConnection * SqlTransaction

    member this.ConnectionString = 
        match this with
        | Literal s -> s
        | NameInConfig name -> Configuration.GetConnectionStringAtRunTime name
        | ``Connection and-or Transaction``(conn, _) -> conn.ConnectionString 

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type ``ISqlCommand Implementation``(connection, commandTimeout, sqlStatement, isStoredProcedure, parameters, resultType, rank, rowMapping: RowMapping, itemTypeName) = 

    let cmd = new SqlCommand(sqlStatement)

    let privateConnection = 
        match connection with
        | Literal value -> 
            cmd.Connection <- new SqlConnection(value)
            true
        | NameInConfig name ->
            let connStr = Configuration.GetConnectionStringAtRunTime name
            cmd.Connection <- new SqlConnection(connStr)
            true
        | ``Connection and-or Transaction``(conn, tran) ->
             cmd.Connection <- conn
             cmd.Transaction <- tran
             false

    do 
        cmd.CommandType <- if isStoredProcedure then CommandType.StoredProcedure else CommandType.Text
        cmd.CommandTimeout <- commandTimeout
    do
        cmd.Parameters.AddRange( parameters)

    let getReaderBehavior() = 
        seq {
            yield CommandBehavior.SingleResult

            if cmd.Connection.State <> ConnectionState.Open && privateConnection
            then
                cmd.Connection.Open() 
                yield CommandBehavior.CloseConnection

            if rank = ResultRank.SingleRow then yield CommandBehavior.SingleRow 
            if resultType = ResultType.DataTable then yield CommandBehavior.KeyInfo
        }
        |> Seq.reduce (|||) 

    let notImplemented _ : _ = raise <| NotImplementedException()

    let execute, asyncExecute, executeSingle, asyncExecuteSingle = 
        match resultType with
        | ResultType.DataReader -> 
            ``ISqlCommand Implementation``.ExecuteReader >> box, 
            ``ISqlCommand Implementation``.AsyncExecuteReader >> box,
            notImplemented, 
            notImplemented
        | ResultType.DataTable ->
            ``ISqlCommand Implementation``.ExecuteDataTable >> box, 
            ``ISqlCommand Implementation``.AsyncExecuteDataTable >> box,
            notImplemented,
            notImplemented
        | ResultType.Records | ResultType.Tuples ->
            match box rowMapping, itemTypeName with
            | null, itemTypeName when Type.GetType(itemTypeName, throwOnError = true) = typeof<Void> ->
                ``ISqlCommand Implementation``.ExecuteNonQuery privateConnection >> box, 
                ``ISqlCommand Implementation``.AsyncExecuteNonQuery privateConnection >> box,
                notImplemented, 
                notImplemented
            | rowMapping, itemTypeName ->
                assert (rowMapping <> null && itemTypeName <> null)
                let itemType = Type.GetType( itemTypeName, throwOnError = true)
                
                let executeHandle = 
                    typeof<``ISqlCommand Implementation``>
                        .GetMethod("ExecuteSeq", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(itemType)
                
                let asyncExecuteHandle = 
                    typeof<``ISqlCommand Implementation``>
                        .GetMethod("AsyncExecuteSeq", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod(itemType)
                        
                executeHandle.Invoke(null, [| rank; rowMapping |]) |> unbox >> box, 
                asyncExecuteHandle.Invoke(null, [| rank; rowMapping |]) |> unbox >> box,
                executeHandle.Invoke(null, [| ResultRank.SingleRow; rowMapping |]) |> unbox >> box, 
                asyncExecuteHandle.Invoke(null, [| ResultRank.SingleRow; rowMapping |]) |> unbox >> box

        | unexpected -> failwithf "Unexpected ResultType value: %O" unexpected

    member this.CommandTimeout = cmd.CommandTimeout

    interface ISqlCommand with

        member this.Execute parameters = execute(cmd, getReaderBehavior, parameters)
        member this.AsyncExecute parameters = asyncExecute(cmd, getReaderBehavior, parameters)
        member this.ExecuteSingle parameters = executeSingle(cmd, getReaderBehavior, parameters)
        member this.AsyncExecuteSingle parameters = asyncExecuteSingle(cmd, getReaderBehavior, parameters)

        member this.ToTraceString parameters =  
            ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
            let parameterDefinition (p : SqlParameter) =
                if p.Size <> 0 then
                    sprintf "%s %A(%d)" p.ParameterName p.SqlDbType p.Size
                else
                    sprintf "%s %A" p.ParameterName p.SqlDbType 
            seq {
                
                yield sprintf "exec sp_executesql N'%s'" (cmd.CommandText.Replace("'", "''"))
              
                if cmd.Parameters.Count > 0
                then 
                    yield cmd.Parameters
                        |> Seq.cast<SqlParameter> 
                        |> Seq.map parameterDefinition
                        |> String.concat ","
                        |> sprintf "N'%s'" 

                if parameters.Length > 0 
                then 
                    yield parameters
                        |> Seq.map(fun (name,value) -> sprintf "%s='%O'" name value) 
                        |> String.concat ","
            } |> String.concat "," //Using string.concat to handle annoying case with no parameters

        member this.Raw = cmd
            
    interface IDisposable with
        member this.Dispose() =
            cmd.Dispose()

    static member internal SetParameters(cmd: SqlCommand, parameters: (string * obj)[]) = 
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
                    for rowValues in unbox<seq<obj>> value do
                        table.Rows.Add( rowValues :?> obj[]) |> ignore

            if Convert.IsDBNull p.Value 
            then 
                match p.SqlDbType with
                | SqlDbType.NVarChar -> p.Size <- 4000
                | SqlDbType.VarChar -> p.Size <- 8000
                | _ -> ()

//Execute/AsyncExecute versions

    static member internal ExecuteReader(cmd, getReaderBehavior, parameters) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
        cmd.ExecuteReader( getReaderBehavior())

    static member internal AsyncExecuteReader(cmd, getReaderBehavior, parameters) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
        cmd.AsyncExecuteReader( getReaderBehavior())
    
    static member internal ExecuteDataTable(cmd, getReaderBehavior, parameters) = 
        use reader = ``ISqlCommand Implementation``.ExecuteReader(cmd, getReaderBehavior, parameters) 
        let result = new FSharp.Data.DataTable<DataRow>(null, cmd.Clone())
        result.Load(reader)
        result

    static member internal AsyncExecuteDataTable(cmd, getReaderBehavior, parameters) = 
        async {
            use! reader = ``ISqlCommand Implementation``.AsyncExecuteReader(cmd, getReaderBehavior, parameters) 
            let result = new FSharp.Data.DataTable<DataRow>(null, cmd.Clone())
            result.Load(reader)
            return result
        }

    static member internal ExecuteSeq<'TItem> (rank, rowMapper) = fun(cmd, getReaderBehavior, parameters) -> 
        let xs = ``ISqlCommand Implementation``.ExecuteReader(cmd, getReaderBehavior, parameters) |> Seq.ofReader<'TItem> rowMapper

        if rank = ResultRank.SingleRow 
        then 
            xs |> Seq.toOption |> box
        elif rank = ResultRank.ScalarValue 
        then 
            xs |> Seq.exactlyOne |> box
        else 
            assert (rank = ResultRank.Sequence)
            box xs 
            
    static member internal AsyncExecuteSeq<'TItem> (rank, rowMapper) = fun(cmd, getReaderBehavior, parameters) ->
        let xs = 
            async {
                let! reader = ``ISqlCommand Implementation``.AsyncExecuteReader(cmd, getReaderBehavior, parameters)
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

    static member internal ExecuteNonQuery privateConnection (cmd, _, parameters) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)  
        use openedConnection = cmd.Connection.UseLocally(privateConnection )
        cmd.ExecuteNonQuery() 

    static member internal AsyncExecuteNonQuery privateConnection (cmd, _, parameters) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)  
        async {         
            use _ = cmd.Connection.UseLocally(privateConnection )
            return! cmd.AsyncExecuteNonQuery() 
        }



