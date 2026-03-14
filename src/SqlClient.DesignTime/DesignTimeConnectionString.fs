namespace FSharp.Data.SqlClient

open System.Configuration
open System.IO
open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.RegularExpressions

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

    /// Reads a named connection string from an appsettings.json file.
    /// Handles the standard ASP.NET Core ConnectionStrings format:
    ///   { "ConnectionStrings": { "name": "connection_string" } }
    static member TryReadFromAppSettings(name: string, filePath: string) =
        try
            let json = File.ReadAllText(filePath)
            // Locate the ConnectionStrings object; connection strings don't span multiple levels
            let sectionMatch =
                Regex.Match(json, @"""ConnectionStrings""\s*:\s*\{([^}]+)\}", RegexOptions.Singleline)
            if not sectionMatch.Success then None
            else
                let section = sectionMatch.Groups.[1].Value
                // Extract the named value (handles basic JSON escaping)
                let valueMatch =
                    Regex.Match(section, sprintf @"""%s""\s*:\s*""((?:[^""\\]|\\.)*)""" (Regex.Escape(name)))
                if not valueMatch.Success then None
                else
                    // Unescape common JSON escape sequences in the connection string
                    let raw = valueMatch.Groups.[1].Value
                    let unescaped =
                        Regex.Replace(raw, @"\\(.)", fun m ->
                            match m.Groups.[1].Value with
                            | "\"" -> "\"" | "\\" -> "\\" | "/" -> "/"
                            | "n" -> "\n"  | "r" -> "\r"  | "t" -> "\t"
                            | c -> "\\" + c)
                    Some unescaped
        with _ -> None

    static member private ReadFromXmlConfig(name, configFilename) =
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null ->
            raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename)
        | _, Lazy x ->
            let providerName = if String.IsNullOrEmpty x.ProviderName then "System.Data.SqlClient" else x.ProviderName
            x.ConnectionString, providerName

    static member ReadFromConfig(name, resolutionFolder, fileName) = 
        if fileName <> "" then
            let path = Path.Combine(resolutionFolder, fileName)
            if not <| File.Exists path then
                raise <| FileNotFoundException(sprintf "Could not find config file '%s'." path)
            // Support appsettings.json as an explicit fileName
            if path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) then
                match DesignTimeConnectionString.TryReadFromAppSettings(name, path) with
                | Some value -> value, "System.Data.SqlClient"
                | None ->
                    raise <| KeyNotFoundException(sprintf "Cannot find connection string '%s' in ConnectionStrings section of '%s'." name path)
            else
                DesignTimeConnectionString.ReadFromXmlConfig(name, path)
        else
            // Auto-discovery: try XML config files first (app.config / web.config),
            // then appsettings.json for ASP.NET Core projects.
            // note: these filenames are case sensitive on Linux
            let xmlConfigFile =
                seq { yield "app.config"; yield "App.config"; yield "web.config"; yield "Web.config" }
                |> Seq.map (fun v -> Path.Combine(resolutionFolder, v))
                |> Seq.tryFind File.Exists

            match xmlConfigFile with
            | Some configFilename ->
                DesignTimeConnectionString.ReadFromXmlConfig(name, configFilename)
            | None ->
                let appsettingsFile =
                    seq { yield "appsettings.json"; yield "appsettings.Development.json" }
                    |> Seq.map (fun f -> Path.Combine(resolutionFolder, f))
                    |> Seq.tryFind File.Exists
                match appsettingsFile with
                | Some path ->
                    match DesignTimeConnectionString.TryReadFromAppSettings(name, path) with
                    | Some value -> value, "System.Data.SqlClient"
                    | None ->
                        failwithf "Cannot find connection string '%s' in ConnectionStrings section of '%s'." name path
                | None ->
                    failwithf
                        "Cannot find app.config, web.config, or appsettings.json in '%s'. \
                         For ASP.NET Core projects, add a ConnectionStrings section to appsettings.json \
                         or specify the config file explicitly using the ConfigFile parameter."
                        resolutionFolder

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
                    if isNull section then raise <| KeyNotFoundException(message = sprintf "Cannot find name %s in <connectionStrings> section of config file." name)
                    else section.ConnectionString
            @@>

    member this.IsDefinedByLiteral = match this with | Literal _ -> true | _ -> false
