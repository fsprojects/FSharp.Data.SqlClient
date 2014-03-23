namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open System.Threading

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open FSharp.Data.Internals

type ISqlCommand<'TResult,'TNonQueryResult> = 
    abstract AsyncExecute : parameters: (string * obj)[] -> Async<'TResult>
    abstract Execute : parameters: (string * obj)[] -> 'TResult
    abstract AsyncExecuteNonQuery : parameters: (string * obj)[] -> Async<'TNonQueryResult>
    abstract ExecuteNonQuery : parameters: (string * obj)[] -> 'TNonQueryResult

type SqlCommand<'TResult,'TNonQueryResult>( 
                                            connection: SqlConnection, 
                                            command: string, 
                                            commandType: CommandType, 
                                            paramInfos: Parameter list, 
                                            singleRow : bool,
                                            ?mapper: CancellationToken option -> SqlDataReader -> 'TResult,
                                            ?mapperNonQuery: int -> SqlParameterCollection -> 'TNonQueryResult) = 

    let cmd = new SqlCommand(command, connection, CommandType = commandType)
    do
      let toSqlParam (p : Parameter) = 
        let r = SqlParameter(
                        p.Name, 
                        p.TypeInfo.SqlDbType, 
                        Direction = p.Direction,
                        TypeName = p.TypeInfo.UdttName
                    )
        if p.TypeInfo.SqlEngineTypeId = 240 then r.UdtTypeName <- p.TypeInfo.TypeName
        r
      cmd.Parameters.AddRange( paramInfos |> Seq.map toSqlParam |> Array.ofSeq )
            
    let behavior () =
        let connBehavior = 
            if cmd.Connection.State <> ConnectionState.Open then
                //sqlCommand.Connection.StateChange.Add <| fun args -> printfn "Connection %i state change: %O -> %O" (sqlCommand.Connection.GetHashCode()) args.OriginalState args.CurrentState
                cmd.Connection.Open()
                CommandBehavior.CloseConnection
            else
                CommandBehavior.Default 
        connBehavior
        ||| (if singleRow then CommandBehavior.SingleRow else CommandBehavior.Default)
        ||| CommandBehavior.SingleResult

    let setParameters (parameters : (string * obj)[]) = 
        for name,value in parameters do
            let p = cmd.Parameters.[name]            
            p.Value <- if value = null then DbNull else value
            if p.Value = DbNull && (p.SqlDbType = SqlDbType.NVarChar || p.SqlDbType = SqlDbType.VarChar)
            then p.Size <- if  p.SqlDbType = SqlDbType.NVarChar then 4000 else 8000

//    new(connectionStringOrName, command: string, commandType: CommandType, mapper) = 
//        let connectionStringName, isByName = Configuration.ParseConnectionStringName connectionStringOrName
//        let runTimeConnectionString = 
//                    if isByName then Configuration.GetConnectionStringRunTimeByName connectionStringName
//                    else connectionStringOrName
//        SqlCommand<'TResult>(new SqlConnection(runTimeConnectionString), command, commandType, mapper)
    
    //static factories
    //static DataTable
    //static Tuples
    //static Records

    static member internal GetBody(methodName, specialization, [<ParamArray>] bodyFactoryArgs : obj[]) =
        
        let bodyFactory =   
            let mi = typeof<SqlCommand>.GetMethod(methodName, BindingFlags.NonPublic ||| BindingFlags.Static)
            assert(mi <> null)
            mi.MakeGenericMethod([| specialization |])

        fun(args : Expr list) -> 
            let parameters = Array.append [| box args |] bodyFactoryArgs
            bodyFactory.Invoke(null, parameters) |> unbox

    static member GetDataTable<'T when 'T :> DataRow> sqlDataReader =
        use reader = sqlDataReader
        let result = new FSharp.Data.DataTable<'T>()
        result.Load(reader)
        result


    static member GetTypedSequence (token : CancellationToken option, sqlDataReader : SqlDataReader, rowMapper) =
        seq {
            try 
                while((token.IsNone || not token.Value.IsCancellationRequested) && sqlDataReader.Read()) do
                    yield rowMapper sqlDataReader
            finally
                sqlDataReader.Close()
        }

    interface ISqlCommand<'TResult, 'TNonQueryResult> with 
        member this.AsyncExecute parameters = 
            assert(mapper.IsSome)          
            setParameters parameters 
            async {
                let! token = Async.CancellationToken                
                let! reader = 
                    try 
                        Async.FromBeginEnd((fun(callback, state) -> cmd.BeginExecuteReader(callback, state, behavior())), cmd.EndExecuteReader)
                    with _ ->
                        cmd.Connection.Close()
                        reraise()
                return mapper.Value (Some token) reader
            }

        member this.Execute parameters = 
            assert(mapper.IsSome)                           
            setParameters parameters      
            let reader = 
                try 
                    cmd.ExecuteReader(behavior())
                with _ ->
                    cmd.Connection.Close()
                    reraise()
            mapper.Value None reader
       
        member this.AsyncExecuteNonQuery parameters = 
            assert(mapperNonQuery.IsSome)                                       
            setParameters parameters  
            async {         
                if cmd.Connection.State <> ConnectionState.Open then cmd.Connection.Open()
                use disposable = cmd.Connection.UseConnection()
                let! rowsAffected = Async.FromBeginEnd(cmd.BeginExecuteNonQuery, cmd.EndExecuteNonQuery) 
                return mapperNonQuery.Value rowsAffected cmd.Parameters
            }

        member this.ExecuteNonQuery parameters = 
            assert(mapperNonQuery.IsSome)                                       
            setParameters parameters  
            if cmd.Connection.State <> ConnectionState.Open then cmd.Connection.Open()
            use disposable = cmd.Connection.UseConnection()
            let rowsAffected = cmd.ExecuteNonQuery() 
            mapperNonQuery.Value rowsAffected cmd.Parameters

