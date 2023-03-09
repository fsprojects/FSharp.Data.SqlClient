﻿namespace FSharp.Data.SqlClient

open System
open System.IO
open System.Configuration
open System.Collections.Generic
open System.Diagnostics

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
            if fileName <> "" then
                let path = Path.Combine(resolutionFolder, fileName)
                if not <| File.Exists path 
                then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
                else path
            else
                // note: these filenames are case sensitive on linux
                let file = 
                  seq { yield "app.config"
                        yield "App.config"
                        yield "web.config"
                        yield "Web.config" }
                  |> Seq.map (fun v -> Path.Combine(resolutionFolder,v))
                  |> Seq.tryFind File.Exists
                match file with
                | None -> failwithf "Cannot find either App.config or Web.config."
                | Some file -> file
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename)
        | _, Lazy x -> 
            let providerName = if String.IsNullOrEmpty x.ProviderName then "Microsoft.Data.SqlClient" else x.ProviderName
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
                let hostProcess = Process.GetCurrentProcess().ProcessName.ToUpper()
                if isHostedExecution 
                    || (Environment.Is64BitProcess && hostProcess = "FSIANYCPU")
                    || (not Environment.Is64BitProcess && hostProcess = "FSI")
                then 
                    value
                else
                    let section = ConfigurationManager.ConnectionStrings.[name]
                    if section = null 
                    then raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of config file." name)
                    else section.ConnectionString
            @@>

    member this.IsDefinedByLiteral = match this with | Literal _ -> true | _ -> false
