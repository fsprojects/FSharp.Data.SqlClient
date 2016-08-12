namespace FSharp.Data

///<summary>Enum describing output type</summary>
type ResultType =
///<summary>Sequence of custom records with properties matching column names and types</summary>
    | Records = 0
///<summary>Sequence of tuples matching column types with the same order</summary>
    | Tuples = 1
///<summary>Typed DataTable <see cref='T:FSharp.Data.DataTable`1'/></summary>
    | DataTable = 2
///<summary>raw DataReader</summary>
    | DataReader = 3

namespace FSharp.Data.SqlClient

open System.Configuration
open System.IO
open System
open System.Threading.Tasks
open System.Collections.Generic

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type internal DesignTimeConnectionString = 
    | Literal of string
    | NameInConfig of name: string * value: string * provider: string

    static member Parse(s: string, resolutionFolder, fileName) =
        match s.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
            | [| "" |] -> invalidArg "ConnectionStringOrName" "Value is empty!"
            | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> 
                let name = tail.Trim()
                let value, provider = DesignTimeConnectionString.ReadFromConfig( name, resolutionFolder, fileName)
                NameInConfig( name, value, provider)
            | _ -> 
                Literal s

    static member ReadFromConfig(name, resolutionFolder, fileName) = 
        let configFilename = 
            if fileName <> "" 
            then
                let path = Path.Combine(resolutionFolder, fileName)
                if not <| File.Exists path 
                then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
                else path
            else
                let appConfig = Path.Combine(resolutionFolder, "app.config")
                let webConfig = Path.Combine(resolutionFolder, "web.config")

                if File.Exists appConfig then appConfig
                elif File.Exists webConfig then webConfig
                else failwithf "Cannot find either app.config or web.config."
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename)
        | _, Lazy x -> 
            let providerName = if String.IsNullOrEmpty x.ProviderName then "System.Data.SqlClient" else x.ProviderName
            x.ConnectionString, providerName

    member this.Value = 
        match this with
        | Literal value -> value
        | NameInConfig(_, value, _) -> value

    member this.RunTimeValueExpr isHostedExecution = 
        match this with
        | Literal value -> <@@ value @@>
        | NameInConfig(name, value, _) -> 
            <@@ 
                if isHostedExecution || System.Diagnostics.Process.GetCurrentProcess().ProcessName.ToLower() = "fsi"
                then 
                    value
                else
                    let section = ConfigurationManager.ConnectionStrings.[name]
                    if section = null 
                    then raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of config file." name)
                    else section.ConnectionString
            @@>

    member this.IsDefinedByLiteral = match this with | Literal _ -> true | _ -> false

//this is mess. Clean up later.
type Configuration = {
    ResultsetRuntimeVerification: bool
}   

namespace FSharp.Data

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<AutoOpen>]
module Configuration = 
    let private guard = obj()
    let private current = ref { SqlClient.Configuration.ResultsetRuntimeVerification = false }

    type SqlClient.Configuration with
        static member Current 
            with get() = lock guard <| fun() -> !current
            and set value = lock guard <| fun() -> current := value
