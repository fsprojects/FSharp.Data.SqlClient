// Binaries that have XML documentation (in a corresponding generated XML file)
let referenceBinaries = [ ]
// Web site location for the generated documentation
let website = "/FSharp.Data.SqlClient"

let githubLink = "http://github.com/fsprojects/FSharp.Data.SqlClient"

// Specify more information about your project
let info =
  [ "project-name", "FSharp.Data.SqlClient"
    "project-logo-url", "img/logo.png"
    "project-author", "Dmitry Morozov, Dmitry Sevastianov"
    "project-summary", "SqlCommandProvider provides statically typed access to input parameters and result set of T-SQL command in idiomatic F# way. SqlProgrammabilityProvider exposes Stored Procedures, User-Defined Types and User-Defined Functions in F# code."
    "project-github", githubLink
    "project-nuget", "http://www.nuget.org/packages/FSharp.Data.SqlClient" ]

#load "../../.paket/load/net46/Build/FSharp.Formatting.fsx"
#load "../../.paket/load/net46/Build/FAKE.Lib.fsx"

open Fake
open System.IO
open Fake.IO.FileSystemOperators
open FSharp.Literate
open FSharp.MetadataFormat

// see https://github.com/fsharp/FAKE/issues/1579#issuecomment-306580820
let execContext = Fake.Core.Context.FakeExecutionContext.Create false (Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)) []
Fake.Core.Context.setExecutionContext (Fake.Core.Context.RuntimeContext.Fake execContext)

#if RELEASE
let root = website
#else
let root = "file://" + (__SOURCE_DIRECTORY__ @@ "../output")
#endif

// Paths with template/source/output locations
let bin        = __SOURCE_DIRECTORY__ @@ "../../bin"
let content    = __SOURCE_DIRECTORY__ @@ "../content"
let output     = __SOURCE_DIRECTORY__ @@ "../output"
let files      = __SOURCE_DIRECTORY__ @@ "../files"
let templates  = __SOURCE_DIRECTORY__ @@ "templates"
let formatting = __SOURCE_DIRECTORY__ @@ "../../packages/Build/FSharp.Formatting/"
let docTemplate = formatting @@ "templates/docpage.cshtml"

// Where to look for *.csproj templates (in this order)
let layoutRoots =
  [ templates; formatting @@ "templates"
    formatting @@ "templates/reference" ]

// Copy static files and CSS + JS from F# Formatting
let copyFiles () =
  Fake.IO.Shell.copyRecursive files output true |> Fake.Core.Trace.logItems "Copying file: "
  Fake.IO.Directory.ensure (output @@ "content")
  Fake.IO.Shell.copyRecursive (formatting @@ "styles") (output @@ "content") true 
    |> Fake.Core.Trace.logItems "Copying styles and scripts: "

// Build API reference from XML comments
let buildReference () =
  Fake.IO.Shell.cleanDir (output @@ "reference")
  for lib in referenceBinaries do
    MetadataFormat.Generate
      ( bin @@ lib, output @@ "reference", layoutRoots, 
        parameters = ("root", root)::info,
        sourceRepo = githubLink @@ "tree/master",
        sourceFolder = __SOURCE_DIRECTORY__ @@ ".." @@ "..",
        publicOnly = true )

// Build documentation from `fsx` and `md` files in `docs/content`
let buildDocumentation () =
  let subdirs = Directory.EnumerateDirectories(content, "*", SearchOption.AllDirectories)
  for dir in Seq.append [content] subdirs do
    let sub = if dir.Length > content.Length then dir.Substring(content.Length + 1) else "."
    Literate.ProcessDirectory
      ( dir, docTemplate, output @@ sub, replacements = ("root", root)::info,
        layoutRoots = layoutRoots )

// Generate
copyFiles()
buildDocumentation()
buildReference()