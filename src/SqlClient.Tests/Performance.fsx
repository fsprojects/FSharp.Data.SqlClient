open System
open System.Data
open System.Data.SqlClient

let conn = new SqlConnection("Data Source=.;Initial Catalog=tempdb;Integrated Security=True") 
conn.Open()

let command = new SqlCommand("select * from Posts where Id = @id", conn)
let param = command.Parameters.Add("@id", SqlDbType.Int) 
 
let asyncTest () = 
    Async.FromBeginEnd((fun(callback, state) -> command.BeginExecuteReader(callback, state, CommandBehavior.Default)), command.EndExecuteReader) 
    |> Async.RunSynchronously

let testAsync () = command.ExecuteReaderAsync().Result
        
let testSync () = command.ExecuteReader()

let test f = 
    let start = DateTime.Now
    for i in 1..1000 do 
        param.Value <- i
        let reader : SqlDataReader = f()
        reader.Close()
    (DateTime.Now - start).Milliseconds 

test asyncTest |> printfn  "AsyncExecute: %A ms"       
test testAsync |> printfn  "ExecuteAsync: %A ms"       
test testSync |> printfn  "Sync: %A ms"       

