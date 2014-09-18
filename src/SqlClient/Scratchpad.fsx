
#r "System.Transactions"

open System
open System.Data
open System.Data.SqlClient

let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Open()

//let cmd = new SqlCommand("sp_help", conn)
//cmd.Parameters.AddWithValue("@objname", "[dbo].[ufnGetContactInformation]") |> ignore
let cmd = new SqlCommand("dbo.Swap", conn, CommandType = CommandType.StoredProcedure)
SqlCommandBuilder.DeriveParameters(cmd)
cmd.Parameters.["@input"].Value <- 12
cmd.Parameters.["@output"].Value <- DBNull.Value
cmd.Parameters.["@nullOutput"].Value <- DBNull.Value
cmd.Parameters.["@nullStringOutput"].Value <- DBNull.Value
[ for p in cmd.Parameters -> sprintf "\n%A, %A: [%O]" p.ParameterName p.Direction p.Value] |> String.concat "," |> printfn "Params: %A" 
using(cmd.ExecuteReader()) (fun reader -> reader |> Seq.cast<IDataRecord> |> Seq.map (fun x -> x.[1], x,[2]) |> Seq.toArray)
[ for p in cmd.Parameters -> sprintf "%A: [%O]" p.ParameterName p.Value] |> String.concat "," |> printfn "Params: %A" 

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
        


