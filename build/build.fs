open Fake.DotNet
open System
open System.Linq
open System.IO
open System.Xml.Linq
open Fun.Build
open Fake.Core
open Fake.Tools.Git
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.DotNet

let makeRootPath path =
    DirectoryInfo(__SOURCE_DIRECTORY__ </> ".." </> path).FullName

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
let authors = [ "Dmitry Morozov, Dmitry Sevastianov" ]
let summary = "SqlClient F# type providers"

let description =
    "SqlCommandProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way.\nSqlProgrammabilityProvider exposes Stored Procedures, User-Defined Types and User-Defined Functions in F# code."

let tags = "F# fsharp data typeprovider sql"

let gitHome = "https://github.com/fsprojects"
let gitName = "FSharp.Data.SqlClient"


// Read release notes & version info from RELEASE_NOTES.md
let release =
    File.ReadLines(makeRootPath "RELEASE_NOTES.md") |> Fake.Core.ReleaseNotes.parse

let version = release.AssemblyVersion
let releaseNotes = release.Notes |> String.concat "\n"

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Target.create "AssemblyInfo" (fun _ ->
    [ makeRootPath "src/SqlClient/AssemblyInfo.fs", "SqlClient", project, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp
            fileName
            [ AssemblyInfo.Title title
              AssemblyInfo.Product project
              AssemblyInfo.Description summary
              AssemblyInfo.Version version
              AssemblyInfo.FileVersion version
              AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ])

    [ makeRootPath "src/SqlClient.DesignTime/AssemblyInfo.fs", "SqlClient.DesignTime", designTimeProject, summary ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        AssemblyInfoFile.createFSharp
            fileName
            [ AssemblyInfo.Title title
              AssemblyInfo.Product project
              AssemblyInfo.Description summary
              AssemblyInfo.Version version
              AssemblyInfo.FileVersion version
              AssemblyInfo.InternalsVisibleTo "SqlClient.Tests" ]))

let slnPath = makeRootPath "SqlClient.sln"
let testProjectsSlnPath = makeRootPath "TestProjects.sln"
let testSlnPath = makeRootPath "Tests.sln"
let testDir = makeRootPath "tests"
let testProjectPath = makeRootPath "tests/SqlClient.Tests/SqlClient.Tests.fsproj"

let msbuilDisableBinLog args =
    { args with
        DisableInternalBinLog = true }

let runDotnetBuild sln =
    stage $"dotnet build '%s{sln}'" { run $"dotnet build {sln} -c Release --tl" }


Target.create "Clean" (fun _ ->
    Shell.cleanDirs [ "bin"; "temp" ]

    let dnDefault (args: DotNet.Options) =
        { args with
            Verbosity = Some DotNet.Verbosity.Quiet }

    DotNet.exec dnDefault "clean" slnPath |> ignore
    DotNet.exec dnDefault "clean" testProjectsSlnPath |> ignore
    DotNet.exec dnDefault "clean" testSlnPath |> ignore
    ())

Target.create "CleanDocs" (fun _ -> Shell.cleanDirs [ "docs/output" ])



let dotnetBuildDisableBinLog (args: DotNet.BuildOptions) =
    { args with
        MSBuildParams =
            { args.MSBuildParams with
                DisableInternalBinLog = true
                Verbosity = Some Quiet } }

let dnDefault =
    dotnetBuildDisableBinLog
    >> DotNet.Options.withVerbosity (Some DotNet.Verbosity.Quiet)
    >> DotNet.Options.withCustomParams (Some "--tl")

Target.create "Build" (fun _ ->
    DotNet.build
        (fun args ->
            { args with
                Configuration = DotNet.Release }
            |> dnDefault)
        slnPath)

open System.Data.SqlClient
open System.IO.Compression
open Fake.DotNet.Testing

Target.create "DeployTestDB" (fun _ ->
    let testsSourceRoot = Path.GetFullPath("tests/SqlClient.Tests")
    let mutable database = None
    let mutable testConnStr = None
    let mutable conn = None

    pipeline "DeployTestDB" {

        stage "adjust config file connection strings" {
            run (fun ctx ->
                let appConfigPath = testsSourceRoot @@ "app.config"

                let connStrValue =
                    let gitHubActionSqlConnectionString =
                        System.Environment.GetEnvironmentVariable "GITHUB_ACTION_SQL_SERVER_CONNECTION_STRING"

                    if String.IsNullOrWhiteSpace gitHubActionSqlConnectionString then
                        // Read current value directly from XML
                        let doc = XDocument.Load(appConfigPath)

                        doc.Root.Element("connectionStrings").Elements("add")
                        |> Seq.find (fun el -> el.Attribute(XName.Get "name").Value = "AdventureWorks")
                        |> fun el -> el.Attribute(XName.Get "connectionString").Value
                    else
                        // Write the new connection string directly into the XML file
                        let doc = XDocument.Load(appConfigPath)

                        let el =
                            doc.Root.Element("connectionStrings").Elements("add")
                            |> Seq.find (fun el -> el.Attribute(XName.Get "name").Value = "AdventureWorks")

                        el.SetAttributeValue(XName.Get "connectionString", gitHubActionSqlConnectionString)
                        doc.Save(appConfigPath)
                        gitHubActionSqlConnectionString

                let connStr = SqlConnectionStringBuilder connStrValue
                testConnStr <- Some connStr
                database <- Some connStr.InitialCatalog

                conn <-
                    connStr.InitialCatalog <- ""
                    let cnx = new SqlConnection(string connStr)
                    cnx.Open()
                    Some cnx)
        }

        stage "attach database to server" {
            run (fun ctx ->

                //attach
                let dbIsMissing =
                    let query =
                        sprintf "SELECT COUNT(*) FROM sys.databases WHERE name = '%s'" database.Value

                    use cmd = new SqlCommand(query, conn.Value)
                    cmd.ExecuteScalar() = box 0

                // Guard: if the DB exists but lacks the Person schema it is an empty shell
                // (e.g. created by install-sql-server-action before a real .bak restore).
                let dbIsEmpty =
                    if dbIsMissing then
                        false
                    else
                        let query =
                            sprintf "SELECT COUNT(*) FROM [%s].sys.schemas WHERE name = 'Person'" database.Value

                        use cmd = new SqlCommand(query, conn.Value)
                        cmd.ExecuteScalar() = box 0

                if dbIsEmpty then
                    failwithf
                        "Database '%s' exists but is empty (missing AdventureWorks schema). \
Please restore AdventureWorks2012 from backup before running the tests."
                        database.Value

                if dbIsMissing then
                    let dataFileName = "AdventureWorks2012_Data"
                    //unzip
                    let sourceMdf = testsSourceRoot @@ (dataFileName + ".mdf")

                    if File.Exists(sourceMdf) then
                        File.Delete(sourceMdf)

                    ZipFile.ExtractToDirectory(testsSourceRoot @@ (dataFileName + ".zip"), testsSourceRoot)

                    let dataPath =
                        use cmd =
                            new SqlCommand("SELECT SERVERPROPERTY('InstanceDefaultDataPath')", conn.Value)

                        cmd.ExecuteScalar() |> string

                    do
                        let destFileName = dataPath @@ Path.GetFileName(sourceMdf)
                        File.Copy(sourceMdf, destFileName, overwrite = true)
                        File.Delete(sourceMdf)

                        use cmd =
                            conn.Value.CreateCommand(
                                CommandText =
                                    sprintf
                                        "CREATE DATABASE [%s] ON ( FILENAME = N'%s' ) FOR ATTACH"
                                        database.Value
                                        destFileName
                            )

                        cmd.ExecuteNonQuery() |> ignore)
        }

        //create extra object to test corner case
        stage "patch adventure works" {
            run (fun ctx ->
                use _ = conn.Value
                let script = File.ReadAllText(testsSourceRoot @@ "extensions.sql")

                for batch in script.Split([| "GO"; "go" |], StringSplitOptions.RemoveEmptyEntries) do
                    try
                        use cmd = conn.Value.CreateCommand(CommandText = batch)
                        cmd.ExecuteNonQuery() |> ignore
                    with e ->
                        let message = $"error while patching test db:\n{e.Message}\n{batch}"
                        printfn $"{message}"
                        raise (Exception(message, e))

            )
        }

        runImmediate
    })

let funBuildRestore stageName sln =
    stage $"dotnet restore %s{stageName} '{sln}'" { run $"dotnet restore {sln} --tl" }

let funBuildRunDotnet stageName sln =
    stage $"dotnet build %s{stageName}" { run $"dotnet build {sln} -c Release --tl" }

Target.create "BuildTestProjects" (fun _ ->
    pipeline "BuildTestProjects" {
        funBuildRestore "test projects sln" testProjectsSlnPath
        funBuildRunDotnet "test projects sln" testProjectsSlnPath
        runImmediate
    })

// --------------------------------------------------------------------------------------
// Run the unit tests
Target.create "RunTests" (fun _ ->

    let runTests () =
        let dnTestOptions framework (args: DotNet.TestOptions) =
            { args with
                Framework = Some framework
                Common = args.Common
                NoBuild = true
                MSBuildParams =
                    { args.MSBuildParams with
                        DisableInternalBinLog = true } }

        try
            DotNet.test (dnTestOptions "net9.0") testProjectPath
        with ex ->
            Trace.log (sprintf "Test exception: %A" ex)
            raise ex

    pipeline "RunTests" {
        funBuildRestore "test sln" testSlnPath
        funBuildRunDotnet "test sln" testSlnPath

        stage "run tests" { run (fun ctx -> runTests ()) }
        runImmediate
    })

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->

    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
    let nugetPath = "packages/build/NuGet.CommandLine/tools/NuGet.exe"

    Fake.DotNet.NuGet.NuGet.NuGet
        (fun p ->
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
        "nuget/SqlClient.nuspec")

// --------------------------------------------------------------------------------------
// Generate the documentation

Target.create "GenerateDocs" (fun _ ->
    pipeline "GenerateDocs" {
        stage "Generate Docs" { run $"dotnet fsi docs/tools/generate.fsx --define:RELEASE" }
        runImmediate
    })

Target.create "ServeDocs" (fun _ ->
    fakeiisexpress.HostStaticWebsite id (__SOURCE_DIRECTORY__ @@ @"docs\output\")
    |> ignore

    fakeiisexpress.OpenUrlInBrowser "http://localhost:8080")

Target.create "ReleaseDocs" (fun _ ->
    Repository.clone "" (gitHome + "/" + gitName + ".git") "temp/gh-pages"
    Branches.checkoutBranch "temp/gh-pages" "gh-pages"
    Shell.copyRecursive "docs/output" "temp/gh-pages" true |> printfn "%A"
    CommandHelper.runSimpleGitCommand "temp/gh-pages" "add ." |> printfn "%s"

    let cmd =
        sprintf """commit -a -m "Update generated documentation for version %s""" release.NugetVersion

    CommandHelper.runSimpleGitCommand "temp/gh-pages" cmd |> printfn "%s"
    Branches.push "temp/gh-pages")

Target.create "Release" ignore

Target.create "Format" (fun _ -> DotNet.exec id "fantomas" "src tests build" |> ignore)

Target.create "CheckFormat" (fun _ ->
    let result = DotNet.exec id "fantomas" "src tests build --check"

    if not result.OK then
        failwith "Fantomas check failed – run 'dotnet fantomas src tests build' to fix formatting.")

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

open Fake.Core.TargetOperators // for ==>

"Clean"
==> "CheckFormat"
==> "AssemblyInfo"
==> "Build"
==> "DeployTestDB"
==> "BuildTestProjects"
==> "RunTests"
==> "All"
|> ignore<string>

"All" ==> "NuGet" ==> "Release" |> ignore<string>

"All" ==> "CleanDocs" ==> "GenerateDocs" ==> "ReleaseDocs" |> ignore<string>

Target.runOrDefault "All"
