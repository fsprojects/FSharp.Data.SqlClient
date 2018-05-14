// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#I @"packages/FAKE/tools"
#r @"packages/FAKE/tools/FakeLib.dll"

open System.IO
open Fake 
open System
open Fake.DotNet
open Fake.DotNet.AssemblyInfoFile
open Fake.Core.Target
open Fake.Core.TargetOperators
open Fake.Core
open Fake.IO
open Fake.Git

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let files includes = 
  { BaseDirectory = __SOURCE_DIRECTORY__
    Includes = includes
    Excludes = [] } 

// Information about the project to be used at NuGet and in AssemblyInfo files
let project = "FSharp.Data.SqlClient"
let authors = ["Dmitry Morozov, Dmitry Sevastianov"]
let summary = "SqlClient F# type providers"
let description = "SqlCommandProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way.\nSqlProgrammabilityProvider exposes Stored Procedures, User-Defined Types and User-Defined Functions in F# code."
let tags = "F# fsharp data typeprovider sql"
      
let gitHome = "https://github.com/fsprojects"
let gitName = "FSharp.Data.SqlClient"

// Read release notes & version info from RELEASE_NOTES.md
let release = 
    File.ReadLines "RELEASE_NOTES.md" 
    |> ReleaseNotes.parse

let version = release.AssemblyVersion
let releaseNotes = release.Notes |> String.concat "\n"
let testDir = "bin"

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Core.Target.create "AssemblyInfo" (fun _ ->
    [ "src/SqlClient/AssemblyInfo.fs", "SqlClient", project, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        createFSharp fileName
           [ DotNet.AssemblyInfo.Title title
             DotNet.AssemblyInfo.Product project
             DotNet.AssemblyInfo.Description summary
             DotNet.AssemblyInfo.Version version
             DotNet.AssemblyInfo.FileVersion version
             DotNet.AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ] )
)

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages
Core.Target.create "RestorePackages" (fun _ ->
    !! "./**/packages.config"
    |> Seq.iter (Fake.DotNet.NuGet.Restore.RestorePackage (fun p -> { p with ToolPath = "./.nuget/NuGet.exe" }))
)

Core.Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

Core.Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

let mutable dotnetExePath = "dotnet"
let dotnetcliVersion = "2.1.200"
let installedDotnetSDKVersion = DotNetCli.getVersion ()

Core.Target.create "InstallDotNetCore" (fun _ ->
    dotnetExePath <- DotNetCli.InstallDotNetSDK dotnetcliVersion
    Environment.SetEnvironmentVariable("DOTNET_EXE_PATH", dotnetExePath)
)

// --------------------------------------------------------------------------------------
// Build library 
Core.Target.create "Build" (fun _ ->
//    DotNetCli.Restore(fun p -> 
//        { p with 
//            Project = "src/SqlClient/SqlClient.fsproj"
//            NoCache = true })
//
//    DotNetCli.Build(fun p -> 
//        { p with 
//            Project = "src/SqlClient/SqlClient.fsproj"
//            Configuration = "Debug" // "Release" 
//        })
    
    ["netstandard2.0"; "net461"]
    |> List.iter (fun target ->
    let outDir = __SOURCE_DIRECTORY__ </> "bin" </> target
    DotNetCli.Publish (fun p -> 
        { p with Output = outDir
                 Framework = target
                 WorkingDir = "src/SqlClient/" 
        })
    )
)

#r "System.Data"
#r "System.Transactions"
#r "System.Configuration"
#r "System.IO.Compression"
#r "System.IO.Compression.FileSystem"

open System.Data.SqlClient
open System.Configuration
open System.IO.Compression
open System.Numerics

Core.Target.create "DeployTestDB" (fun _ ->
    let testsSourceRoot = Path.GetFullPath(@"src\SqlClient.Tests")
    let map = ExeConfigurationFileMap()
    map.ExeConfigFilename <- testsSourceRoot @@ "app.config"
    let connStr = 
        let x = 
            ConfigurationManager
                .OpenMappedExeConfiguration(map, ConfigurationUserLevel.None)
                .ConnectionStrings
                .ConnectionStrings.["AdventureWorks"]
                .ConnectionString
        SqlConnectionStringBuilder(x)

    let database = connStr.InitialCatalog
    use conn = 
        connStr.InitialCatalog <- ""
        new SqlConnection(string connStr)

    conn.Open()

    do //attach
        let dbIsMissing = 
            let query = sprintf "SELECT COUNT(*) FROM sys.databases WHERE name = '%s'" database
            use cmd = new SqlCommand(query, conn)
            cmd.ExecuteScalar() = box 0

        if dbIsMissing
        then 
            let dataFileName = "AdventureWorks2012_Data"
            //unzip
            let sourceMdf = testsSourceRoot @@ (dataFileName + ".mdf")
    
            if File.Exists(sourceMdf) then File.Delete(sourceMdf)
    
            ZipFile.ExtractToDirectory(testsSourceRoot @@ (dataFileName + ".zip"), testsSourceRoot)


            let dataPath = 
                use cmd = new SqlCommand("SELECT SERVERPROPERTY('InstanceDefaultDataPath')", conn)
                cmd.ExecuteScalar() |> string
            do
                let destFileName = dataPath @@ Path.GetFileName(sourceMdf) 
                File.Copy(sourceMdf, destFileName, overwrite = true)
                File.Delete( sourceMdf)
                use cmd = new SqlCommand(Connection = conn)
                cmd.CommandText <- sprintf "CREATE DATABASE [%s] ON ( FILENAME = N'%s' ) FOR ATTACH" database destFileName
                cmd.ExecuteNonQuery() |> ignore

    do //create extra object to test corner case
        let script = File.ReadAllText(testsSourceRoot @@ "extensions.sql")
        for batch in script.Split([|"GO"|], StringSplitOptions.RemoveEmptyEntries) do
            use cmd = new SqlCommand(batch, conn)
            cmd.ExecuteNonQuery() |> ignore
)

Core.Target.create "BuildTests" (fun _ ->
    DotNetCli.Build (fun p -> { p with Configuration = "Release"
                                       Project = "Tests.sln" } )
)

// --------------------------------------------------------------------------------------
// Run the unit tests 
Core.Target.create "RunTests" (fun _ ->
    !! (testDir + "/*.Tests.dll")
        |> Testing.XUnit2.run (fun p -> 
            {p with 
                ShadowCopy = false
                XmlOutputPath = Some testDir })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Core.Target.create "NuGet" (fun _ ->
//    CopyDir @"bin" "src/SqlClient/bin/Debug" allFiles
//    CopyDir @"bin" "src/SqlClient/bin/Release" allFiles
    
    CopyDir @"temp/lib" "bin" allFiles

    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
    let nugetPath = ".nuget/nuget.exe"

    DotNet.NuGet.NuGet.NuGetPack (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = releaseNotes
            Tags = tags
            WorkingDir = "temp"
            OutputPath = "nuget"
            ToolPath = nugetPath
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            Dependencies = [] })
        "nuget/SqlClient.nuspec"
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Core.Target.create "GenerateDocs" (fun _ ->
    executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"] [] |> ignore
)

Core.Target.create "ReleaseDocs" (fun _ ->
    Tools.Git.Repository.clone "" (gitHome + "/" + gitName + ".git") "temp/gh-pages"
    Tools.Git.Branches.checkoutBranch "temp/gh-pages" "gh-pages"
    Shell.copyRecursive "docs/output" "temp/gh-pages" true |> printfn "%A"
    Tools.Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" "add ." |> printfn "%s"
    let cmd = sprintf """commit -a -m "Update generated documentation for version %s""" release.NugetVersion
    Tools.Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" cmd |> printfn "%s"
    Tools.Git.Branches.push "temp/gh-pages"
)

Core.Target.create "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Core.Target.create "All" DoNothing

"Clean"
  ==> "RestorePackages"
  ==> "AssemblyInfo"
  =?> ("InstallDotNetCore", installedDotnetSDKVersion <> dotnetcliVersion)
  ==> "Build"
  //==> "DeployTestDB"
  //==> "BuildTests"
  //==> "RunTests"
  ==> "All"

"All" 
  ==> "NuGet"
  ==> "Release"

"All" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"

Core.Target.runOrDefault "All"

