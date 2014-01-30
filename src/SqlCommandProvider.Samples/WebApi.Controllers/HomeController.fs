namespace WebApi

open System
open System.Data.SqlClient
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Configuration
open System.Dynamic
open System.Collections.Generic

open DataAccess

module SqlCommand = 
    let inline create() : 'a = 
        let adventureWorks = WebConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString
        let designTimeConnectionString = (^a : (static member get_ConnectionStringOrName : unit -> string) ())
        let database = SqlConnectionStringBuilder(designTimeConnectionString).InitialCatalog
        if database = "AdventureWorks2012"
        then (^a : (new : string -> ^a) adventureWorks)    
        else failwithf "Unrecognized command type %s" typeof<'a>.FullName

type HomeController() =
    inherit ApiController()

    let connectionString = WebConfigurationManager.ConnectionStrings.["AdventureWorks2012"].ConnectionString

    member this.Get() = this.Get(7L, System.DateTime.Parse "2002-06-01")

    //http://localhost:61594/?top=4&sellStartDate=2002-07-01
    member this.Get(top, sellStartDate) =
        async {
            let cmd : QueryProductsAsTuples = SqlCommand.create()
            let! data = cmd.AsyncExecute(top = top, SellStartDate = sellStartDate)
            return this.Request.CreateResponse(HttpStatusCode.OK, data)
        } |> Async.StartAsTask
