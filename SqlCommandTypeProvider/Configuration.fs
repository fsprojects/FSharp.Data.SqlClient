module FSharp.Data.SqlClient.Configuration

open System.Configuration
open System.IO

let getConnectionString(resolutionFolder, connectionString, connectionStringName, configFile)  =
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

