#I "../../packages/FSharp.Formatting.2.1.6/lib/net40"
#I "../../packages/RazorEngine.3.3.0/lib/net40/"
#r "../../packages/Microsoft.AspNet.Razor.2.0.30506.0/lib/net40/System.Web.Razor.dll"
#r "../../packages/FAKE/tools/FakeLib.dll"
#r "RazorEngine.dll"
#r "FSharp.Literate.dll"
#r "FSharp.CodeFormat.dll"
#r "FSharp.MetadataFormat.dll"
open Fake
open System.IO
open Fake.FileHelper
open FSharp.Literate
open FSharp.MetadataFormat


// Specify more information about your project
let info =
  [ "project-name", "SqlcommandTypeProvider"
    "project-author", "Dmitry Morozov, Dmitry Sevastianov"
    "project-summary", "The SqlCommand type provider wraps over sql query to provide strongly typed parameters and various ways of deserializing output, including Tuples and DTOs"
    "project-github", "http://github.com/dmitry-a-morozov/FSharp.Data.SqlCommandTypeProvider"
    "project-nuget", "http://www.nuget.org/packages/SqlCommandTypeProvider" ]

let root = "file://" + (__SOURCE_DIRECTORY__ @@ "../output")

// Paths with template/source/output locations
let bin        = __SOURCE_DIRECTORY__ @@ "../../bin"
let content    = __SOURCE_DIRECTORY__ @@ "../content"
let output     = __SOURCE_DIRECTORY__ @@ "../output"
let test        = __SOURCE_DIRECTORY__ @@ "../../Tests/Test.fsx"
let templates  = __SOURCE_DIRECTORY__ @@ "templates"
let formatting = __SOURCE_DIRECTORY__ @@ "../../packages/FSharp.Formatting.2.1.6/"
let docTemplate = formatting @@ "templates/docpage.cshtml"

let layoutRoots =
  [ templates; formatting @@ "templates"
    formatting @@ "templates/reference" ]

    // Copy static files and CSS + JS from F# Formatting
let copyFiles () =
  CopyFile output test 
  ensureDirectory (output @@ "content")
  CopyRecursive (formatting @@ "content") (output @@ "content") true 
    |> Log "Copying styles and scripts: "

copyFiles()
Literate.ProcessScriptFile( test, docTemplate, output @@ "Test.html", replacements = ("root", root)::info, layoutRoots = layoutRoots)
