
open System
open System.Data
open System.Data.SqlClient

let cmdBehaviour = 
    seq {
        if false
        then 
            yield CommandBehavior.CloseConnection
        yield CommandBehavior.SingleRow
    }
    |> Seq.fold (|||) CommandBehavior.SingleResult

let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()

let cmd = new SqlCommand("select 'HAHA' as name, 1 as val where 1 = 1", conn)
let reader = cmd.ExecuteReader(CommandBehavior.CloseConnection ||| CommandBehavior.SingleRow)
let xs = seq {
    use closeReader = reader
    while reader.Read() do
        yield [
            for i = 0 to reader.FieldCount - 1 do
                if not(reader.IsDBNull(i)) 
                then yield reader.GetName(i), reader.GetValue(i)
        ] |> Map.ofList 
}

reader |> Seq.cast<IDataRecord> |> Seq.map( fun x -> Map.ofList [ for i = 0 to x.FieldCount - 1 do yield x.GetName(i), x.GetValue(i) ])
conn.State

let myFucc() = 
    use __ = { new System.IDisposable with member __.Dispose() = printfn "Bye-bye!" }
    ()
myFucc()

#r "Microsoft.SqlServer.ConnectionInfo"
#r "Microsoft.SqlServer.Management.Sdk.Sfc" 
#r "Microsoft.SqlServer.Smo"

open Microsoft.SqlServer.Management.Smo

let server = new Server(@"(LocalDb)\v11.0")
let db = server.Databases.["AdventureWorks2012"]
let sp = db.StoredProcedures.["uspSearchCandidateResumes"]
for p in sp.Parameters do printfn "Name: %s. defaulf value: %s" p.Name p.DefaultValue
sp.Parameters.[3].DefaultValue

type ISqlCommand<'TResult> = 
    abstract AsyncExecute : parameters: (string * obj)[] -> Async<'TResult>
    abstract Execute : parameters: (string * obj)[] -> Async<'TResult>

type SqlCommand<'TResult>(connection: SqlConnection, mapper: SqlDataReader -> 'TResult) = 

    new(connectionStringOrName) = SqlCommand<'TResult>(new SqlConnection())
    
    //static factories
    //static DataTable
    //static Tuples
    //static Records

    interface ISqlCommand<'TResult> with 
        member this.AsyncExecute parameters = raise <| NotImplementedException()
        member this.Execute parameters = raise <| NotImplementedException()


open System.Reflection

type A<'T> = abstract Foo : a:'T -> 'T
type Bar<'T>() = 
    member this.Foo () = ()
    interface A<'T> with member this.Foo a = a

let t = typedefof<_ Bar>.MakeGenericType([|typeof<string>|])
t.GetMethods()
t.GetMethod("Foo")

open Quotations.Patterns
let expr = <@@ unbox<int>(box 5) @@>
match expr with Call(None, mi, _) -> mi.DeclaringType.AssemblyQualifiedName | _ -> "hehe"

let boxMethod = System.Type.GetType( "Microsoft.FSharp.Core.Operators, FSharp.Core").GetMethod("Box")