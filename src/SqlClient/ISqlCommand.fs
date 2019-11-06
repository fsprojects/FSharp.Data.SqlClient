namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient
open System.Reflection

open FSharp.Data.SqlClient
open System.Linq


[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type ISqlCommand = 
    
    abstract Execute: parameters: (string * obj)[] -> obj
    abstract AsyncExecute: parameters: (string * obj)[] -> obj
    abstract ExecuteSingle: parameters: (string * obj)[] -> obj
    abstract AsyncExecuteSingle: parameters: (string * obj)[] -> obj

    abstract ToTraceString: parameters: (string * obj)[] -> string

    abstract Raw: SqlCommand with get

module Seq = 

    let internal toOption source =  
        match source |> Seq.truncate 2 |> Seq.toArray with
        | [||] -> None
        | [| x |] -> Some x
        | _ -> invalidArg "source" "The input sequence contains more than one element."

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type RowMapping = obj[] -> obj


[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type DesignTimeConfig = {
    SqlStatement: string
    IsStoredProcedure: bool 
    Parameters: SqlParameter[]
    ResultType: ResultType
    Rank: ResultRank
    RowMapping: RowMapping
    ItemTypeName: string
    ExpectedDataReaderColumns: (string * string)[]
}

type internal Connection = Choice<string, SqlConnection, SqlTransaction>

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type ``ISqlCommand Implementation``(cfg: DesignTimeConfig, connection: Connection, commandTimeout) = 
    let cmd = new SqlCommand(cfg.SqlStatement, CommandTimeout = commandTimeout)
    let manageConnection = 
        match connection with
        | Choice1Of3 connectionString -> 
            cmd.Connection <- new SqlConnection(connectionString)
            true
        | Choice2Of3 instance -> 
            cmd.Connection <- instance
            false
        | Choice3Of3 tran -> 
            cmd.Transaction <- tran 
            cmd.Connection <- tran.Connection
            false

    do
        cmd.CommandType <- if cfg.IsStoredProcedure then CommandType.StoredProcedure else CommandType.Text
        cmd.Parameters.AddRange( cfg.Parameters)

    let getReaderBehavior() = 
        seq {
            yield CommandBehavior.SingleResult

            if cmd.Connection.State <> ConnectionState.Open && manageConnection
            then
                cmd.Connection.Open() 
                yield CommandBehavior.CloseConnection

            if cfg.Rank = ResultRank.SingleRow then yield CommandBehavior.SingleRow 
        }
        |> Seq.reduce (|||) 

    let notImplemented _ : _ = raise <| NotImplementedException()

    let execute, asyncExecute, executeSingle, asyncExecuteSingle = 
        match cfg.ResultType with
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
            match box cfg.RowMapping, cfg.ItemTypeName with
            | null, null ->
                ``ISqlCommand Implementation``.ExecuteNonQuery manageConnection >> box, 
                ``ISqlCommand Implementation``.AsyncExecuteNonQuery manageConnection >> box,
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
                        
                executeHandle.Invoke(null, [| cfg.Rank; cfg.RowMapping |]) |> unbox >> box, 
                asyncExecuteHandle.Invoke(null, [| cfg.Rank; cfg.RowMapping |]) |> unbox >> box,
                executeHandle.Invoke(null, [| ResultRank.SingleRow; cfg.RowMapping |]) |> unbox >> box, 
                asyncExecuteHandle.Invoke(null, [| ResultRank.SingleRow; cfg.RowMapping |]) |> unbox >> box

        | unexpected -> failwithf "Unexpected ResultType value: %O" unexpected

    member this.CommandTimeout = cmd.CommandTimeout

    interface ISqlCommand with

        member this.Execute parameters = execute(cmd, getReaderBehavior, parameters, cfg.ExpectedDataReaderColumns)
        member this.AsyncExecute parameters = asyncExecute(cmd, getReaderBehavior, parameters, cfg.ExpectedDataReaderColumns)
        member this.ExecuteSingle parameters = executeSingle(cmd, getReaderBehavior, parameters, cfg.ExpectedDataReaderColumns)
        member this.AsyncExecuteSingle parameters = asyncExecuteSingle(cmd, getReaderBehavior, parameters, cfg.ExpectedDataReaderColumns)

        member this.ToTraceString parameters =  
            ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
            let parameterDefinition (p : SqlParameter) =
                // decimal uses precision and scale instead of size
                if List.contains p.SqlDbType [SqlDbType.Money; SqlDbType.SmallMoney; SqlDbType.Decimal] then
                    // maximum size is 38
                    sprintf "%s %A(%u,%u)" p.ParameterName p.SqlDbType p.Precision p.Scale
                    
                // tinyint and Xml have size 1 and -1 respectively, but MSSQL will throw if they are specified
                elif p.Size <> 0 && 
                   p.SqlDbType <> SqlDbType.Xml && 
                   p.SqlDbType <> SqlDbType.TinyInt then
                   
                    sprintf "%s %A(%d)" p.ParameterName p.SqlDbType p.Size
                else
                    sprintf "%s %A" p.ParameterName p.SqlDbType 
             
            // helper map to resolve each parameter's target type
            let getSqlDbType name = 
               let lookup = Map.ofSeq <| Seq.zip (parameters     |> Seq.map (fun (name, value) -> name))
                                                 (cmd.Parameters |> Seq.cast<SqlParameter> |> Seq.map (fun p -> p.SqlDbType))
               Map.find name lookup
            
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
                        |> Seq.map(fun (name,value) ->   
                            // NULL isn't escaped
                            match value with
                            | null | :? DBNull ->  sprintf "%s=NULL" name
                            | nonNullValue ->
                                let printedValue =                                 
                                    match nonNullValue with
                                    // print dates with high precision (SQL datetimeoffset, datetime2) in roundtrip ISO8601 format "O"
                                    | :? System.DateTimeOffset as d -> d.ToString("O")
                                    | :? System.DateTime as d when getSqlDbType d = SqlDbType.DateTime2 -> d.ToString("O")
                                    // print dates with low precision (SQL datetime) in legacy format
                                    | :? System.DateTime as d when getSqlDbType d <> SqlDbType.DateTime2 -> d.ToString("yyyy-MM-ddTHH:mm:ss.fff")                          
                                    // print timespans in constant format "c
                                    | :? System.TimeSpan as t -> t.ToString("c")
                                    // print numeric values in culture-invariant format
                                    | :? decimal as n -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? double as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? single as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? bigint as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? uint64 as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? int64 as n   -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)                                
                                    | :? uint32 as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)                                
                                    | :? int as n     -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)                                
                                    | :? uint16 as n  -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)                                
                                    | :? int16 as n   -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? byte as n    -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    | :? sbyte as n   -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)      
                                    | v -> sprintf "%O" v
                                // escapes the resulting value, with Unicode notation
                                sprintf "%s=N'%s'" name (printedValue.Replace("'", "''"))
                        )                            
                        |> String.concat ","
            } |> String.concat "," //Using string.concat to handle annoying case with no parameters

        member this.Raw = cmd
            
    interface IDisposable with
        member this.Dispose() =
            if manageConnection then
              cmd.Connection.Dispose()
            cmd.Dispose()

    static member internal SetParameters(cmd: SqlCommand, parameters: (string * obj)[]) = 
        for name, value in parameters do
            let p = cmd.Parameters.[name]            

            if p.Direction.HasFlag(ParameterDirection.Input)
            then 
                match value with
                | null ->
                    p.Value <- DBNull.Value 
                | _ ->
                    match p.SqlDbType with 
                    | SqlDbType.Structured -> 
                        // TODO: Maybe make this lazy?

                        //p.Value <- value |> unbox |> Seq.cast<Microsoft.SqlServer.Server.SqlDataRecord>

                        //done via reflection because not implemented on Mono
                        
                        let sqlDataRecordType = typeof<SqlCommand>.Assembly.GetType("Microsoft.SqlServer.Server.SqlDataRecord", throwOnError = true)
                        let records = typeof<Linq.Enumerable>.GetMethod("Cast").MakeGenericMethod(sqlDataRecordType).Invoke(null, [| value |]) 
                        let hasAny = 
                            typeof<Linq.Enumerable>
                                .GetMethods(BindingFlags.Static ||| BindingFlags.Public)
                                .First(fun m -> m.Name = "Any" && m.GetParameters().Count() = 1)
                                .MakeGenericMethod(sqlDataRecordType).Invoke(null, [| records |]) :?> bool
                        p.Value <- if not hasAny then null else records
                    | _ -> p.Value <- value
                            
            elif p.Direction.HasFlag(ParameterDirection.Output) && value :? Array then
                p.Size <- (value :?> Array).Length

    //Execute/AsyncExecute versions

    static member internal VerifyResultsetColumns(cursor: SqlDataReader, expected) = 
        if Configuration.Current.ResultsetRuntimeVerification
        then 
            if cursor.FieldCount < Array.length expected
            then 
                let message = sprintf "Expected at least %i columns in result set but received only %i." expected.Length cursor.FieldCount
                cursor.Close()
                invalidOp message

            for i = 0 to expected.Length - 1 do
                let expectedName, expectedType = fst expected.[i], Type.GetType( snd expected.[i], throwOnError = true)
                let actualName, actualType = cursor.GetName( i), cursor.GetFieldType( i)
                if actualName <> expectedName || actualType <> expectedType
                then 
                    let message = sprintf """Expected column [%s] of type "%A" at position %i (0-based indexing) but received column [%s] of type "%A".""" expectedName expectedType i actualName actualType
                    cursor.Close()
                    invalidOp message

    static member internal ExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
        let cursor = cmd.ExecuteReader( getReaderBehavior())
        ``ISqlCommand Implementation``.VerifyResultsetColumns(cursor, expectedDataReaderColumns)
        cursor

    static member internal AsyncExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) = 
        async {
            ``ISqlCommand Implementation``.SetParameters(cmd, parameters)
            let! cursor = cmd.AsyncExecuteReader( getReaderBehavior())
            ``ISqlCommand Implementation``.VerifyResultsetColumns(cursor, expectedDataReaderColumns)
            return cursor
        }
    
    static member internal ExecuteDataTable(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) = 
        use cursor = ``ISqlCommand Implementation``.ExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) 
        let result = new FSharp.Data.DataTable<DataRow>(cmd)
        result.Load(cursor)
        result

    static member internal AsyncExecuteDataTable(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) = 
        async {
            use! reader = ``ISqlCommand Implementation``.AsyncExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) 
            let result = new FSharp.Data.DataTable<DataRow>(cmd)
            result.Load(reader)
            return result
        }

    static member internal ExecuteSeq<'TItem> (rank, rowMapper) = fun(cmd: SqlCommand, getReaderBehavior, parameters, expectedDataReaderColumns) -> 
        let hasOutputParameters = cmd.Parameters |> Seq.cast<SqlParameter> |> Seq.exists (fun x -> x.Direction.HasFlag( ParameterDirection.Output))

        if not hasOutputParameters
        then 
            let xs = Seq.delay <| fun() -> 
                ``ISqlCommand Implementation``
                    .ExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns)
                    .MapRowValues<'TItem>( rowMapper)

            if rank = ResultRank.SingleRow 
            then 
                xs |> Seq.toOption |> box
            elif rank = ResultRank.ScalarValue 
            then 
                xs |> Seq.exactlyOne |> box
            else 
                assert (rank = ResultRank.Sequence)
                box xs 
        else
            let resultset = 
                ``ISqlCommand Implementation``
                    .ExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns)
                    .MapRowValues<'TItem>( rowMapper)
                    |> Seq.toList

            if hasOutputParameters
            then
                for i = 0 to parameters.Length - 1 do
                    let name, _ = parameters.[i]
                    let p = cmd.Parameters.[name]
                    if p.Direction.HasFlag( ParameterDirection.Output)
                    then 
                        parameters.[i] <- name, p.Value

            box resultset
            
    static member internal AsyncExecuteSeq<'TItem> (rank, rowMapper) = fun(cmd, getReaderBehavior, parameters, expectedDataReaderColumns) ->
        let xs = 
            async {
                let! reader = ``ISqlCommand Implementation``.AsyncExecuteReader(cmd, getReaderBehavior, parameters, expectedDataReaderColumns)
                return reader.MapRowValues<'TItem>( rowMapper)
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

    static member internal ExecuteNonQuery manageConnection (cmd, _, parameters, _) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)  
        use openedConnection = cmd.Connection.UseLocally(manageConnection )
        let recordsAffected = cmd.ExecuteNonQuery() 
        for i = 0 to parameters.Length - 1 do
            let name, _ = parameters.[i]
            let p = cmd.Parameters.[name]
            if p.Direction.HasFlag( ParameterDirection.Output)
            then 
                parameters.[i] <- name, p.Value
        recordsAffected

    static member internal AsyncExecuteNonQuery manageConnection (cmd, _, parameters, _) = 
        ``ISqlCommand Implementation``.SetParameters(cmd, parameters)  
        async {         
            use _ = cmd.Connection.UseLocally(manageConnection )
            return! cmd.AsyncExecuteNonQuery() 
        }
