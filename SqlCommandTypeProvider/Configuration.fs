module FSharp.Data.SqlClient.Configuration

open System.Configuration
open System.IO
open System

let getConnectionString resolutionFolder connectionString connectionStringName configFile  =
    if connectionString <> "" then connectionString
    else
        if connectionStringName = "" then failwithf "Either ConnectionString or ConnectionStringName is required"
        let getMappedConfig file = 
            let path = Path.Combine(resolutionFolder, file)            
            let map = new ExeConfigurationFileMap()
            map.ExeConfigFilename <- path
            ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings.[connectionStringName]
        let configSection = 
            match configFile, ConfigurationManager.ConnectionStrings.[connectionStringName] with
            | "", null -> 
                let c = getMappedConfig "app.config"
                if c = null then getMappedConfig "web.config" else c
            | "", c -> c
            | file, _ -> getMappedConfig file
        if configSection = null 
        then failwithf "Connection string %s is not found." connectionStringName
        else configSection.ConnectionString


let private invalidChars = [ 
    yield! Path.GetInvalidFileNameChars() 
    yield! Path.GetInvalidPathChars()] |> set

let parseTextAtDesignTime (commandTextOrPath : string) resolutionFolder invalidate =
    if commandTextOrPath |> Seq.exists (fun c-> invalidChars.Contains c)
    then commandTextOrPath
    else
        let path = Path.Combine(resolutionFolder, commandTextOrPath)
        if File.Exists(path) |> not 
        then commandTextOrPath
        else
            if  Path.GetExtension(commandTextOrPath) <> ".sql" then failwith "Only files with .sql extension are supported"
            let watcher = new FileSystemWatcher(Filter = commandTextOrPath, Path = resolutionFolder)
            watcher.Changed.Add(fun _ -> invalidate())
            watcher.EnableRaisingEvents <- true                    
            File.ReadAllText(path)
            
  