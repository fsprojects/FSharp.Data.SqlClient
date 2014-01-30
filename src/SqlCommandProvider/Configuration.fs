namespace FSharp.Data.Experimental.Internals

open System.Configuration
open System.IO
open System

type Configuration() =    
    static let invalidChars = Path.GetInvalidPathChars() |> set

    static member ParseTextAtDesignTime(commandTextOrPath : string, resolutionFolder, invalidateCallback) =
        if commandTextOrPath |> Seq.exists (fun c-> invalidChars.Contains c)
        then commandTextOrPath, None
        else
            let path = Path.Combine(resolutionFolder, commandTextOrPath)
            if File.Exists(path) |> not 
            then commandTextOrPath, None
            else
                if  Path.GetExtension(commandTextOrPath) <> ".sql" then failwith "Only files with .sql extension are supported"
                let watcher = new FileSystemWatcher(Filter = commandTextOrPath, Path = resolutionFolder)
                watcher.Changed.Add(fun _ -> invalidateCallback())
                watcher.EnableRaisingEvents <- true                    
                File.ReadAllText(path), Some watcher

    static member ReadConnectionStringFromConfigFileByName(name: string, resolutionFolder, fileName) =
        let path = Path.Combine(resolutionFolder, fileName)
        if not <| File.Exists path then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
        let map = ExeConfigurationFileMap( ExeConfigFilename = path)
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name path
        | _, Lazy x -> x.ConnectionString

    static member GetConnectionStringRunTimeByName(name: string) = 
        let section = ConfigurationManager.ConnectionStrings.[name]
        if section = null 
        then failwithf "Cannot find name %s in <connectionStrings> section of config file." name
        else section.ConnectionString



            
  