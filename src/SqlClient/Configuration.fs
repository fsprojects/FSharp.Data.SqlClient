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

type Configuration() =    
    static let invalidPathChars = HashSet(Path.GetInvalidPathChars())
    static let invalidFileChars = HashSet(Path.GetInvalidFileNameChars())

    static member GetValidFileName (file:string, resolutionFolder:string) = 
        try 
            if (file.Contains "\n") || (resolutionFolder.Contains "\n") then None else
            let f = Path.Combine(resolutionFolder, file)
            if invalidPathChars.Overlaps (Path.GetDirectoryName f) ||
               invalidFileChars.Overlaps (Path.GetFileName f) then None 
            else 
               // Canonicalizing the path may throw on bad input, the check above does not cover every error.
               Some (Path.GetFullPath f) 
        with _ -> 
            None

    static member ParseTextAtDesignTime(commandTextOrPath : string, resolutionFolder, invalidateCallback) =
        match Configuration.GetValidFileName (commandTextOrPath, resolutionFolder) with
        | Some path when File.Exists path ->
                if Path.GetExtension(path) <> ".sql" then failwith "Only files with .sql extension are supported"
                let watcher = new FileSystemWatcher(Filter = Path.GetFileName path, Path = Path.GetDirectoryName path)
                watcher.Changed.Add(fun _ -> invalidateCallback())
                watcher.Renamed.Add(fun _ -> invalidateCallback())
                watcher.Deleted.Add(fun _ -> invalidateCallback())
                watcher.EnableRaisingEvents <- true   
                let task = Task.Factory.StartNew(fun () -> 
                        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                        use reader = new StreamReader(stream)
                        reader.ReadToEnd())
                if not (task.Wait(TimeSpan.FromSeconds(1.))) then failwithf "Couldn't read command from file %s" path
                task.Result, Some watcher 
        | _ -> commandTextOrPath, None

    static member ParseConnectionStringName(s: string) =
        match s.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
            | [| "" |] -> invalidArg "ConnectionStringOrName" "Value is empty!"
            | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> tail.Trim(), true
            | _ -> s, false

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
                else failwithf "Cannot find either app.config or web.config."
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename
        | _, Lazy x -> x.ConnectionString

    static member GetConnectionStringAtRunTime(name: string) = 
        let section = ConfigurationManager.ConnectionStrings.[name]
        if section = null 
        then failwithf "Cannot find name %s in <connectionStrings> section of config file." name
        else section.ConnectionString



            
  