open Fake.DotNet
open System
open System.Linq
open System.IO
open Fun.Build
open Fake.Core
open Fake.Tools.Git
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.DotNet
let makeRootPath path = DirectoryInfo( __SOURCE_DIRECTORY__ </> ".." </> path).FullName

let execContext =
  System.Environment.GetCommandLineArgs().Skip 1
  |> Seq.toList
  |> Fake.Core.Context.FakeExecutionContext.Create false "build.fs"

Fake.Core.Context.setExecutionContext (Fake.Core.Context.RuntimeContext.Fake execContext)

Environment.CurrentDirectory <- makeRootPath "."

let files includes = 
  { BaseDirectory = makeRootPath "."
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
    File.ReadLines (makeRootPath "RELEASE_NOTES.md")
    |> Fake.Core.ReleaseNotes.parse

let version = release.AssemblyVersion
let releaseNotes = release.Notes |> String.concat "\n"

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Target.create "AssemblyInfo" (fun _ ->
    [ makeRootPath "src/SqlClient/AssemblyInfo.fs", "SqlClient", project, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp fileName
           [ AssemblyInfo.Title              title
             AssemblyInfo.Product            project
             AssemblyInfo.Description        summary
             AssemblyInfo.Version            version
             AssemblyInfo.FileVersion        version
             AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ] )

    [ makeRootPath "src/SqlClient.DesignTime/AssemblyInfo.fs", "SqlClient.DesignTime", designTimeProject, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp fileName
           [ AssemblyInfo.Title              title
             AssemblyInfo.Product            project
             AssemblyInfo.Description        summary
             AssemblyInfo.Version            version
             AssemblyInfo.FileVersion        version
             AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ] )
)

let slnPath = makeRootPath "SqlClient.sln"
let testProjectsSlnPath = makeRootPath "TestProjects.sln"
let testSlnPath = makeRootPath "Tests.sln"
let testDir = makeRootPath "tests"
let testProjectPath = makeRootPath "tests/SqlClient.Tests/SqlClient.Tests.fsproj"

let msBuildPaths extraPaths =
    [
        @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"
        @"C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\current\Bin"
        @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\current\Bin"
        @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\current\Bin"
        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin"
        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\MSBuild\current\Bin"
        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\current\Bin"
        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\current\Bin"
        yield! extraPaths
    ] 
    |> List.map (fun p -> Path.Combine(p, "MSBuild.exe"))
    |> List.find File.Exists

let fsiExePath =
  [
    @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsiAnyCpu.exe"
    @"C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsiAnyCpu.exe"
    @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsiAnyCpu.exe"
    @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsiAnyCpu.exe"
  ] 
  |> List.tryFind File.Exists
  

let msbuilDisableBinLog args = { args with DisableInternalBinLog = true }

let runMsBuild project =
        Fake.DotNet.MSBuild.build 
            (fun args ->
                let properties = 
                  [ yield "Configuration", "Release"
                    for n,v in args.Properties do 
                      if n <> "Configuration" then 
                        yield n,v
                  ]
                { args
                    with ToolPath = msBuildPaths [args.ToolPath]
                         Properties = properties
                         Verbosity = Some MSBuildVerbosity.Quiet
                } |> msbuilDisableBinLog) project


Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
    let dnDefault (args: DotNet.Options) = { args with Verbosity = Some DotNet.Verbosity.Quiet }
    DotNet.exec dnDefault "clean" slnPath |> ignore
    DotNet.exec dnDefault "clean" testProjectsSlnPath |> ignore
    DotNet.exec dnDefault "clean" testSlnPath |> ignore
    ()
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)



let dotnetBuildDisableBinLog (args: DotNet.BuildOptions) =
    { args with MSBuildParams = { args.MSBuildParams with DisableInternalBinLog = true; Verbosity = Some Quiet } } 

let dnDefault =
  dotnetBuildDisableBinLog 
  >> DotNet.Options.withVerbosity (Some DotNet.Verbosity.Quiet)
  >> DotNet.Options.withCustomParams (Some "--tl")

Target.create "Build" (fun _ ->
    DotNet.build
        (fun args -> { args with Configuration = DotNet.Release } |> dnDefault)
        slnPath
)

open System.Data.SqlClient
open System.Configuration
open System.IO.Compression
open Fake.DotNet.Testing

Target.create "DeployTestDB" (fun _ ->
    let testsSourceRoot = Path.GetFullPath(@"tests\SqlClient.Tests")
    let map = ExeConfigurationFileMap()
    map.ExeConfigFilename <- testsSourceRoot @@ "app.config"
    let testConfigFile = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None)
    let connStr = 
      let connStr = 
        let gitHubActionSqlConnectionString = System.Environment.GetEnvironmentVariable "GITHUB_ACTION_SQL_SERVER_CONNECTION_STRING"
        if String.IsNullOrWhiteSpace gitHubActionSqlConnectionString then
          testConfigFile
            .ConnectionStrings
            .ConnectionStrings.["AdventureWorks"]
            .ConnectionString
        else
          // we run under Github Actions, update the test config file connection string.
          testConfigFile
            .ConnectionStrings
            .ConnectionStrings.["AdventureWorks"]
            .ConnectionString <- gitHubActionSqlConnectionString
          
          testConfigFile.Save()

          gitHubActionSqlConnectionString
      SqlConnectionStringBuilder connStr

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
                use cmd = conn.CreateCommand(CommandText = sprintf "CREATE DATABASE [%s] ON ( FILENAME = N'%s' ) FOR ATTACH" database destFileName)
                cmd.ExecuteNonQuery() |> ignore

    do //create extra object to test corner case
        let script = File.ReadAllText(testsSourceRoot @@ "extensions.sql")
        for batch in script.Split([|"GO";"go"|], StringSplitOptions.RemoveEmptyEntries) do
            use cmd = conn.CreateCommand(CommandText = batch)
            cmd.ExecuteNonQuery() |> ignore
)

let funBuildRestore stageName sln =
    stage $"dotnet restore %s{stageName} '{sln}'" {
        run $"dotnet restore {sln} --tl" 
    }
let funBuildRunMSBuild stageName sln =
    let msbuild = $"\"{msBuildPaths [] }\""
    stage $"run MsBuild %s{stageName}" {
        run $"{msbuild} {sln} -verbosity:quiet --tl"
    }

Target.create "BuildTestProjects" (fun _ ->
    pipeline "BuildTestProjects" {
        funBuildRestore    "test projects sln" testProjectsSlnPath
        funBuildRunMSBuild "test projects sln" testProjectsSlnPath
        runImmediate
    }
)

// --------------------------------------------------------------------------------------
// Run the unit tests 
Target.create "RunTests" (fun _ -> 
    
    let runTests () =
      let dnTestOptions framework (args: DotNet.TestOptions) = 
        { args with 
            Framework = Some framework
            Common = args.Common
            NoBuild = true
            MSBuildParams = { args.MSBuildParams with DisableInternalBinLog = true } 
        }
      try 
          DotNet.test (dnTestOptions "net462") testSlnPath
          DotNet.test (dnTestOptions "netcoreapp3.1") testProjectPath
      with
      | ex ->
          Trace.log (sprintf "Test exception: %A" ex)
          raise ex
    
    pipeline "RunTests" {
        funBuildRestore    "test sln" testSlnPath
        funBuildRunMSBuild "test sln" testSlnPath

        stage "run tests" { 
          run (fun ctx -> runTests())
        }
        runImmediate
    }
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
    let fsiPath =
      match fsiExePath with
      | Some fsiExePath -> $"\"{fsiExePath}\""
      | None -> failwith "FSIAnyCpu.exe wasn't found"
    pipeline "GenerateDocs" {
        stage "Generate Docs" {
             run $"{fsiPath} docs/tools/generate.fsx --define:RELEASE" 
        }
        runImmediate
    } 
)

Target.create "ServeDocs" (fun _ -> 
  fakeiisexpress.HostStaticWebsite id (__SOURCE_DIRECTORY__ @@ @"docs\output\") |> ignore
  fakeiisexpress.OpenUrlInBrowser "http://localhost:8080"
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

Target.create "Release" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

open Fake.Core.TargetOperators // for ==>

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "DeployTestDB"  
  ==> "BuildTestProjects"  
  ==> "RunTests"
  ==> "All"
  |> ignore<string>

"All" 
  ==> "NuGet"
  ==> "Release"
  |> ignore<string>

"All" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"
  |> ignore<string>

Target.runOrDefault "All"
