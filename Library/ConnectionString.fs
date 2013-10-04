module FSharp.NYC.Tutorial.ConnectionString

open System
open System.Configuration
open System.IO
open System.Xml.Linq

let resolve resolutionFolder connectionString connectionStringName configFile dataDirectory  =
    let connectionString = 
        if connectionString <> "" then connectionString
        else
            if connectionStringName = "" then failwithf "Either ConnectionString or ConnectionStringName is required"                 
            let path = Path.Combine(resolutionFolder, configFile)            
            let map = new ExeConfigurationFileMap()
            printfn "%s" path
            map.ExeConfigFilename <- path
            ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings.[connectionStringName].ConnectionString

    connectionString
