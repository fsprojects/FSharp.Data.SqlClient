open Fake.DotNet
// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#r @"packages/build/FAKE/tools/FakeLib.dll"
#load "tools/fakexunithelper.fsx" // helper for xunit 1 is gone, work around by having our own copy for now
#load "tools/fakeiisexpress.fsx"  // helper for iisexpress is not ready, work around by having our own copy for now

open System
open System.IO
open Fake.Core
open Fake.Git
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.DotNet

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let files includes = 
  { BaseDirectory = __SOURCE_DIRECTORY__
    Includes = includes
    Excludes = [] } 

// Information about the project to be used at NuGet and in AssemblyInfo files
let project = "FSharp.Data.SqlClient"
let designTimeProject = "FSharp.Data.SqlClient.DesignTime"
let authors = ["Dmitry Morozov, Dmitry Sevastianov"]
let summary = "SqlClient F# type providers"
let description = "SqlCommandProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way.\nSqlProgrammabilityProvider exposes Stored Procedures, User-Defined Types and User-Defined Functions in F# code."
let tags = "F# fsharp data typeprovider sql"
      
let gitHome = "https://github.com/fsprojects"
let gitName = "FSharp.Data.SqlClient"

// Read release notes & version info from RELEASE_NOTES.md
let release = 
    File.ReadLines "RELEASE_NOTES.md" 
    |> Fake.Core.ReleaseNotes.parse

let version = release.AssemblyVersion
let releaseNotes = release.Notes |> String.concat "\n"

let install = lazy DotNet.install DotNet.Versions.Release_2_1_402
let inline dnDefault arg = DotNet.Options.lift install.Value arg

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Target.create "AssemblyInfo" (fun _ ->
    [ "src/SqlClient/AssemblyInfo.fs", "SqlClient", project, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp fileName
           [ AssemblyInfo.Title              title
             AssemblyInfo.Product            project
             AssemblyInfo.Description        summary
             AssemblyInfo.Version            version
             AssemblyInfo.FileVersion        version
             AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ] )

    [ "src/SqlClient.DesignTime/AssemblyInfo.fs", "SqlClient.DesignTime", designTimeProject, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp fileName
           [ AssemblyInfo.Title              title
             AssemblyInfo.Product            project
             AssemblyInfo.Description        summary
             AssemblyInfo.Version            version
             AssemblyInfo.FileVersion        version
             AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ] )
)

let slnPath = "SqlClient.sln"
let testProjectsSlnPath = "TestProjects.sln"
let testSlnPath = "Tests.sln"
let testProjectPath = "tests/SqlClient.Tests/SqlClient.Tests.fsproj"

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
    DotNet.exec dnDefault "clean" slnPath |> ignore
    DotNet.exec dnDefault "clean" testProjectsSlnPath |> ignore
    DotNet.exec dnDefault "clean" testSlnPath |> ignore
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

Target.create "Build" (fun _ ->
    DotNet.build
        (fun args -> 
        { 
            args with 
                Configuration = DotNet.Release
                //Common = { args.Common with Verbosity = Some DotNet.Verbosity.Detailed }
        } |> dnDefault)
        slnPath
    
)

#r "System.Data"
#r "System.Transactions"
#r "System.Configuration"
#r "System.IO.Compression"
#r "System.IO.Compression.FileSystem"

open System.Data.SqlClient
open System.Configuration
open System.IO.Compression

Target.create "DeployTestDB" (fun _ ->
    let testsSourceRoot = Path.GetFullPath(@"tests\SqlClient.Tests")
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

Target.create "BuildTestProjects" (fun _ ->
    DotNet.build
        (fun args -> { args with Configuration = DotNet.Release } |> dnDefault)
        testProjectsSlnPath
)

// --------------------------------------------------------------------------------------
// Run the unit tests 
Target.create "RunTests" (fun _ ->   
    // if we don't compile the targets sequentially, we get an error with the generated types:
    // System.IO.IOException: The process cannot access the file 'C:\Users\foo\AppData\Local\Temp\tmpF38.dll' because it is being used by another process.
    try 
        DotNet.test (fun args -> { args with Framework = Some "net461"; Common = args.Common |> dnDefault }) testSlnPath
        DotNet.test (fun args -> { args with Framework = Some "netcoreapp2.0"; Common = args.Common |> dnDefault }) testProjectPath   
    with
    | ex ->
        Trace.log (sprintf "Test exception: %A" ex)
        raise ex
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->

    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
    let nugetPath = "packages/build/NuGet.CommandLine/tools/NuGet.exe"
    
    Fake.DotNet.NuGet.NuGet.NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = version
            ReleaseNotes = releaseNotes
            Tags = tags
            OutputPath = "nuget"
            ToolPath = nugetPath
            AccessKey = Fake.Core.Environment.environVarOrDefault "nugetkey" ""
            Publish = Fake.Core.Environment.hasEnvironVar "nugetkey"
            Dependencies = [] })
        "nuget/SqlClient.nuspec"
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target.create "GenerateDocs" (fun _ ->
    Fake.FSIHelper.executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"] [] |> ignore
)

Target.create "ServeDocs" (fun _ -> 
  Fakeiisexpress.HostStaticWebsite id (__SOURCE_DIRECTORY__ @@ @"docs\output\") |> ignore
  Fakeiisexpress.OpenUrlInBrowser "http://localhost:8080"
)

Target.create "ReleaseDocs" (fun _ ->
    Repository.clone "" (gitHome + "/" + gitName + ".git") "temp/gh-pages"
    Branches.checkoutBranch "temp/gh-pages" "gh-pages"
    Shell.copyRecursive "docs/output" "temp/gh-pages" true |> printfn "%A"
    CommandHelper.runSimpleGitCommand "temp/gh-pages" "add ." |> printfn "%s"
    let cmd = sprintf """commit -a -m "Update generated documentation for version %s""" release.NugetVersion
    CommandHelper.runSimpleGitCommand "temp/gh-pages" cmd |> printfn "%s"
    Branches.push "temp/gh-pages"
)

Target.create "Release" Target.DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" Target.DoNothing

open Fake.Core.TargetOperators // for ==>

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"    
  ==> "DeployTestDB"  
  ==> "BuildTestProjects"
  ==> "RunTests"
  ==> "All"

"All" 
  ==> "NuGet"
  ==> "Release"

"All" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"

Target.runOrDefault "All"
