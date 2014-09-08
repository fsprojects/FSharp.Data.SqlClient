
#r "System.Transactions"

open System
open System.Data
open System.Data.SqlClient

let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Open()

let procsAndFuncs = conn.GetSchema("Procedures")


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

//DEFAULT PARAMS

#r "Microsoft.SqlServer.TransactSql.ScriptDom"

open Microsoft.SqlServer.TransactSql.ScriptDom
open System.IO
open System.Collections.Generic
open System.Data.SqlClient
open System.Data

let getUspSearchCandidateResumesBody = new SqlCommand("exec sp_helptext 'dbo.ufnGetContactInformation'")
getUspSearchCandidateResumesBody.Connection <- new SqlConnection(@"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True")
getUspSearchCandidateResumesBody.Connection.Open()
let spBody = getUspSearchCandidateResumesBody.ExecuteReader() |> Seq.cast<IDataRecord> |> Seq.map (fun x -> string x.[0]) |> String.concat "\n"

let parser = TSql110Parser(true)
let tsqlReader = new StringReader(spBody)
let mutable errors: IList<ParseError> = null
let fragment = parser.Parse(tsqlReader, &errors)

let paramInfo = List<ProcedureParameter>()

fragment.Accept {
    new TSqlFragmentVisitor() with
        member __.Visit(node : ProcedureParameter) = 
            base.Visit node
            paramInfo.Add node
}

let rec getParamDefaultValue (p: ProcedureParameter) = 
    match p.Value with
    | :? Literal as x ->
        match x.LiteralType with
        | LiteralType.Default | LiteralType.Null -> Some null
        | LiteralType.Integer -> x.Value |> int |> box |> Some
        | LiteralType.Money | LiteralType.Numeric -> x.Value |> decimal |> box |> Some
        | LiteralType.Real -> x.Value |> float |> box |> Some 
        | _ -> None
    //| :? UnaryExpression as expr ->
    | _ -> None 


for p in paramInfo do
    let xxx = p.Value 
    match p.Value with
    | :? Literal as expr -> 
        printfn "%A=%A of type %O" p.VariableName.Value expr.Value expr.LiteralType
    //|:? UnaryExpression as expr

    | _ -> ()
        