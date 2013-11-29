namespace FSharp.Data.SqlClient.Test

open System
open System.Net
open System.Net.Http
open System.Web.Http

[<CLIMutable>]
type HomeRendition = {
    Message : string
    Time : string
}
 
open DataAccess

type HomeController() =
    inherit ApiController()

    [<Literal>]
    let connectionString="Data Source=mitekm-pc2;Initial Catalog=AdventureWorks2012;Integrated Security=True"

    member this.Get() = this.Get(7L, System.DateTime.Parse "2002-06-01")

    //http://localhost:61594/?top=4&sellStartDate=2002-07-01
    member this.Get(top, sellStartDate) =
        async {
            let cmd = QueryProductsAsTuples()
            let! data = cmd.AsyncExecute(top = top, SellStartDate = sellStartDate)
            return this.Request.CreateResponse(HttpStatusCode.OK, data)
        } |> Async.StartAsTask
