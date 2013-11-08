// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#I @"packages/FAKE/tools"
#r @"packages/FAKE/tools/FakeLib.dll"

open System
open System.IO
open Fake 
open Fake.AssemblyInfoFile
open Fake.Git

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let files includes = 
  { BaseDirectories = [__SOURCE_DIRECTORY__]
    Includes = includes
    Excludes = [] } 

// Information about the project to be used at NuGet and in AssemblyInfo files
let project = "SqlCommandTypeProvider"
let authors = ["Dmitry Morozov, Dmitry Sevastianov"]
let summary = "SqlCommand F# type provider"
let description = """
  The SqlCommand type provider wraps over sql query to provide strongly typed 
  parameters and various ways of deserializing output, including Tuples and DTOs"""
let tags = "F# fsharp data typeprovider sql"
      
let gitHome = "https://github.com/fsharp"
let gitName = "FSharp.Data"

// Read release notes & version info from RELEASE_NOTES.md
let release = 
    File.ReadLines "RELEASE_NOTES.md" 
    |> ReleaseNotesHelper.parseReleaseNotes

let version = release.AssemblyVersion
let releaseNotes = release.Notes |> String.concat "\n"

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Target "AssemblyInfo" (fun _ ->
    [ ("src/SqlCommandTypeProvider/AssemblyInfo.fs", "SqlCommandTypeProvider", project, summary) ]
    |> Seq.iter (fun (fileName, title, project, summary) ->
        CreateFSharpAssemblyInfo fileName
           [ Attribute.Title title
             Attribute.Product project
             Attribute.Description summary
             Attribute.Version version
             Attribute.FileVersion version] )
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"]
)

// --------------------------------------------------------------------------------------
// Build library (builds Visual Studio solution, which builds multiple versions
// of the runtime library & desktop + Silverlight version of design time library)

Target "Build" (fun _ ->
    files (["SqlCommandTypeProvider.sln"])
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

Target "BuildTests" (fun _ ->
    files ["SqlCommandTypeProvider.Tests.sln"]
    |> MSBuildReleaseExt "" ([]) "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests 
let testDir = "Tests/*/bin/Release"
let testDlls = !! (testDir + "/Tests.dll")

Target "RunTests" (fun _ ->
    testDlls
        |> xUnit (fun p -> 
            {p with 
                ShadowCopy = false;
                HtmlOutput = true;
                XmlOutput = true;
                OutputDir = testDir })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->

    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
    let nugetPath = ".nuget/nuget.exe"

    NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = version
            ReleaseNotes = releaseNotes
            Tags = tags
            OutputPath = "bin"
            ToolPath = nugetPath
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            Dependencies = [] })
        "nuget/SqlCommandTypeProvider.nuspec"
)

// --------------------------------------------------------------------------------------
// Release Scripts

//Target "UpdateDocs" (fun _ ->
//
//    executeFSI "tools" "build.fsx" [] |> ignore
//
//    DeleteDir "gh-pages"
//    Repository.clone "" "https://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider.git" "gh-pages"
//    Branches.checkoutBranch "gh-pages" "gh-pages"
//    CopyRecursive "docs" "gh-pages" true |> printfn "%A"
//    CommandHelper.runSimpleGitCommand "gh-pages" (sprintf """commit -a -m "Update generated documentation for version %s""" version) |> printfn "%s"
//    Branches.push "gh-pages"
//)
//
//Target "UpdateBinaries" (fun _ ->
//
//    DeleteDir "release"
//    Repository.clone "" "https://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider.git" "release"
//    Branches.checkoutBranch "release" "release"
//    CopyRecursive "bin" "release/bin" true |> printfn "%A"
//    CommandHelper.runSimpleGitCommand "release" (sprintf """commit -a -m "Update binaries for version %s""" version) |> printfn "%s"
//    Branches.push "release"
//)
//
//Target "Release" DoNothing
//
//"UpdateDocs" ==> "Release"
//"UpdateBinaries" ==> "Release"

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean" ==> "AssemblyInfo" ==> "Build"
"Build" ==> "All"
"BuildTests" ==> "All"
"RunTests" ==> "All"
"NuGet" ==> "All"

RunTargetOrDefault "All"
