namespace FSharp.Data.Experimental.Internals

open System.Configuration
open System.IO
open System
open System.Collections.Generic

type Configuration() =    
    static let isInvalidPathChars = HashSet(Path.GetInvalidPathChars())

    static member ParseTextAtDesignTime(commandTextOrPath : string, resolutionFolder, invalidateCallback) =
        if isInvalidPathChars.Overlaps( commandTextOrPath)         
        then commandTextOrPath, None
        else
            let path = Path.Combine(resolutionFolder, commandTextOrPath)
            if File.Exists(path) |> not 
            then commandTextOrPath, None
            else
                if  Path.GetExtension(commandTextOrPath) <> ".sql" then failwith "Only files with .sql extension are supported"
                let watcher = new FileSystemWatcher(Filter = commandTextOrPath, Path = resolutionFolder)
                watcher.Changed.Add(fun _ -> invalidateCallback())
                watcher.Renamed.Add(fun _ -> invalidateCallback())
                watcher.EnableRaisingEvents <- true                    
                File.ReadAllText(path), Some watcher

    static member ParseConnectionStringName(s: string) =
        assert(s.Trim() <> "")
        match s.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
        | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> tail.Trim()
        | _ -> null

    static member ReadConnectionStringFromConfigFileByName(name: string, resolutionFolder, fileName) =

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
                else failwithf "Cannot find neither app.config nor web.config."
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename
        | _, Lazy x -> x.ConnectionString

    static member GetConnectionStringRunTimeByName(name: string) = 
        let section = ConfigurationManager.ConnectionStrings.[name]
        if section = null 
        then failwithf "Cannot find name %s in <connectionStrings> section of config file." name
        else section.ConnectionString



            
  