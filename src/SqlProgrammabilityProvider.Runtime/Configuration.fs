namespace FSharp.Data.Experimental

type ResultType =
    | Tuples = 0
    | Records = 1
    | DataTable = 2
    | Maps = 3

namespace FSharp.Data.Experimental.Runtime

module Configuration = 

    open System.IO
    open System.Configuration

    let getConnectionStringAtRunTime(name: string) =   
        let section = ConfigurationManager.ConnectionStrings.[name]
        if section = null 
        then failwithf "Cannot find name %s in <connectionStrings> section of config file." name
        else section.ConnectionString

