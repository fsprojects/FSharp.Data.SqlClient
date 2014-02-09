namespace WebApi

open System.Data.SqlClient
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Configuration

open DataAccess

module SqlCommand = 
    let inline create() : 'a = 
        let designTimeConnectionString = (^a : (static member get_ConnectionStringOrName : unit -> string) ())

        match designTimeConnectionString with
        | DataAccess.AdventureWorks2012 -> 
            //get connection string at run-time
            let adventureWorks = WebConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString
            //create command instance with connection string override
            (^a : (new : string -> ^a) adventureWorks) 

        | _ -> failwithf "Unrecognized command type %s" typeof<'a>.FullName   


type HomeController() =
    inherit ApiController()

    member this.Get() = this.Get(7L, System.DateTime.Parse "2002-06-01")

    //http://localhost:61594/?top=4&sellStartDate=2002-07-01
    member this.Get(top, sellStartDate) =
        async {
            let cmd : QueryProducts = SqlCommand.create()
            //or get connnection info from web.config
            //let cmd = QueryProducts()
            let! data = cmd.AsyncExecute(top = top, SellStartDate = sellStartDate)
            return this.Request.CreateResponse(HttpStatusCode.OK, data)
        } |> Async.StartAsTask
