namespace FSharp.Data

open System
open System.Data
open System.Data.SqlClient
open System.Reflection

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection
open Samples.FSharp.ProvidedTypes

open FSharp.Data.SqlClient

type ISqlCommand = 
    abstract Execute : parameters: (string * obj)[] -> obj
    abstract AsyncExecute : parameters: (string * obj)[] -> obj
    abstract ToTraceString : parameters: (string * obj)[] -> string
    abstract Raw : SqlCommand with get

type RowMapping = obj[] -> obj

type Connection =
    | String of string
    | Name of string
    | Transaction of SqlTransaction

type SqlCommand<'TItem> (connection, sqlStatement, parameters, resultType, singleRow, rowMapping : RowMapping) = 

    let cmd = new SqlCommand(sqlStatement)
    do 
        match connection with
        | String x -> 
            cmd.Connection <- new SqlConnection(x)
        | Name x ->
            let connStr = Configuration.GetConnectionStringRunTimeByName x
            cmd.Connection <- new SqlConnection(connStr)
        | Transaction t ->
             cmd.Connection <- t.Connection
             cmd.Transaction <- t

    do
        cmd.Parameters.AddRange( parameters)

    let getReaderBehavior = fun() ->
        seq {
            yield CommandBehavior.SingleResult

            if cmd.Connection.State <> ConnectionState.Open 
            then
                cmd.Connection.Open() 
                yield CommandBehavior.CloseConnection

            if singleRow then yield CommandBehavior.SingleRow 

            if resultType = ResultType.DataTable then yield CommandBehavior.KeyInfo
        }
        |> Seq.reduce (|||) 

    let setParameters (cmd : SqlCommand) (parameters : (string * obj)[]) = 
        for name, value in parameters do
            
            let p = cmd.Parameters.[name]            

            if value = null 
            then 
                p.Value <- DbNull 
            else
                if not( p.SqlDbType = SqlDbType.Structured)
                then 
                    p.Value <- value
                else
                    let table : DataTable = unbox p.Value
                    table.Rows.Clear()
                    for rowValues in unbox<seq<obj[]>> value do
                        table.Rows.Add( rowValues) |> ignore

            if p.Value = DbNull 
            then 
                match p.SqlDbType with
                | SqlDbType.NVarChar -> p.Size <- 4000
                | SqlDbType.VarChar -> p.Size <- 8000
                | _ -> ()

    let executeReader parameters = 
        setParameters cmd parameters      
        cmd.ExecuteReader( getReaderBehavior())

    let asyncExecuteReader parameters = 
        setParameters cmd parameters 
        cmd.AsyncExecuteReader( getReaderBehavior())

    let executeDataTable parameters = 
        use reader = executeReader parameters
        let result = new FSharp.Data.DataTable<DataRow>()
        result.Load(reader)
        result

    let asyncExecuteDataTable parameters = 
        async {
            use! reader = asyncExecuteReader parameters
            let result = new FSharp.Data.DataTable<DataRow>()
            result.Load(reader)
            return result
        }

    let seqToOption source =  
        match Seq.toList source with
        | [] -> None
        | [ x ] -> Some x
        | _ -> invalidOp "Single row was expected."

    let readerToSeq (reader : SqlDataReader) = 
        seq {
            use __ = reader
            while reader.Read() do
                let values = Array.zeroCreate reader.FieldCount
                reader.GetValues(values) |> ignore
                yield values |> rowMapping |> unbox<'TItem>
        }
        
    let executeSeq rowMapper parameters = 
        let xs = parameters |> executeReader |> readerToSeq

        if singleRow  
        then xs |> seqToOption |> box
        else box xs 
            
    let asyncExecuteSeq rowMapper parameters = 
        let xs = 
            async {
                let! reader = asyncExecuteReader parameters
                return readerToSeq reader
            }

        if singleRow 
        then
            async {
                let! xs = xs 
                return xs |> seqToOption
            }
            |> box
        else
            box xs 

    let executeNonQuery parameters = 
        setParameters cmd parameters  
        use openedConnection = cmd.Connection.UseConnection()
        cmd.ExecuteNonQuery() 

    let asyncExecuteNonQuery parameters = 
        setParameters cmd parameters  
        async {         
            use openedConnection = cmd.Connection.UseConnection()
            return! cmd.AsyncExecuteNonQuery() 
        }

    let execute, asyncExecute = 
        match resultType with
        | ResultType.Records | ResultType.Tuples->
            if box rowMapping = null
            then
                executeNonQuery >> box , asyncExecuteNonQuery >> box
            else
                executeSeq rowMapping, asyncExecuteSeq rowMapping
        | ResultType.DataTable ->
            executeDataTable >> box, asyncExecuteDataTable >> box
        | ResultType.DataReader -> 
            executeReader >> box, asyncExecuteReader >> box
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
            setParameters clone parameters  
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

