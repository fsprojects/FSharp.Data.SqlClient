module FSharp.Data.SqlClient.ConnectionString

open System
open System.Configuration
open System.IO

let resolve(resolutionFolder, connectionString, connectionStringName, configFile) =
    if connectionString <> "" then connectionString
    else
        if connectionStringName = "" then failwithf "Either ConnectionString or ConnectionStringName is required"                 
        if configFile = ""
        then
            ConfigurationManager.ConnectionStrings.[connectionStringName].ConnectionString
        else
            let path = Path.Combine(resolutionFolder, configFile)            
            let map = new ExeConfigurationFileMap()
            map.ExeConfigFilename <- path
            ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings.[connectionStringName].ConnectionString

