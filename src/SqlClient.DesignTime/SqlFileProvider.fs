namespace FSharp.Data

open System.IO
open Microsoft.FSharp.Core.CompilerServices
open ProviderImplementation.ProvidedTypes
open FSharp.Data.SqlClient
open System.Text 

[<TypeProvider>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type SqlFileProvider(config : TypeProviderConfig) = 
    inherit SingleRootTypeProvider(
        config, 
        "SqlFile", 
        [
            ProvidedStaticParameter("Path", typeof<string>) 
            ProvidedStaticParameter("ResolutionFolder", typeof<string>, "") 
            ProvidedStaticParameter("Encoding", typeof<string>, "") 
        ])

    override __.CreateRootType( assembly, nameSpace, typeName, args) = 
        let path, resolutionFolder, encoding = string args.[0], string args.[1], string args.[2]

        if Path.GetExtension(path) <> ".sql" 
        then failwith "Only files with .sql extension are supported"

        let fullPath = 
            if Path.IsPathRooted( path)
            then 
                path 
            else
                let parent = 
                    if resolutionFolder = "" then config.ResolutionFolder 
                    elif Path.IsPathRooted( resolutionFolder) then resolutionFolder
                    else Path.Combine(config.ResolutionFolder, resolutionFolder)
                Path.Combine( parent, path)

        let typ = 
            lazy 
                let t = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, hideObjectMethods = true)

                let content = 
                    if encoding = "" 
                    then File.ReadAllText( fullPath) 
                    else File.ReadAllText( fullPath, encoding = Encoding.GetEncoding( encoding))

                t.AddMember <| ProvidedField.Literal("Text", typeof<string>, content)

                t

        typ, [| new SingleFileChangeMonitor(fullPath) |]
