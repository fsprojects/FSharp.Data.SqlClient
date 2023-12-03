// https://github.com/fsharp/FAKE/blob/e1378887c41c37d425e134f83424424b76781228/src/legacy/Fake.IIS/IISExpress.fs
// added HostStaticWebsite
module fakeiisexpress

open Fake.Core
open System.Diagnostics
open System
open System.IO
open System.Xml.Linq

/// Options for using IISExpress
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
type IISExpressOptions = 
    { ToolPath : string }

/// IISExpress default parameters - tries to locate the iisexpress.exe
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
let IISExpressDefaults = 
    { ToolPath = 
          let root = 
              if Environment.Is64BitOperatingSystem then 
                  Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
              else Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
          
          Path.Combine(root, "IIS Express", "iisexpress.exe") }

/// Create a IISExpress config file from a given template
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
let createConfigFile (name, siteId : int, templateFileName, path, hostName, port : int) = 
    let xname s = XName.Get(s)
    let uniqueConfigFile = Path.Combine(Path.GetTempPath(), "iisexpress-" + Guid.NewGuid().ToString() + ".config")
    use template = File.OpenRead(templateFileName)
    let xml = XDocument.Load(template)
    let sitesElement = xml.Root.Element(xname "system.applicationHost").Element(xname "sites")
    let appElement = 
        XElement
            (xname "site", XAttribute(xname "name", name), XAttribute(xname "id", siteId.ToString()), 
             XAttribute(xname "serverAutoStart", "true"), 
             
             XElement
                 (xname "application", XAttribute(xname "path", "/"), 
                  
                  XElement
                      (xname "virtualDirectory", XAttribute(xname "path", "/"), XAttribute(xname "physicalPath", DirectoryInfo(path).FullName))), 
             
             XElement
                 (xname "bindings", 
                  
                  XElement
                      (xname "binding", XAttribute(xname "protocol", "http"), 
                       XAttribute(xname "bindingInformation", "*:" + port.ToString() + ":" + hostName)),
                       
                  XElement
                      (xname "binding", XAttribute(xname "protocol", "http"), 
                       XAttribute(xname "bindingInformation", "*:" + port.ToString() + ":*"))))
    sitesElement.Add(appElement)
    xml.Save(uniqueConfigFile)
    uniqueConfigFile

#nowarn "44" // obsolete api
/// This task starts the given site in IISExpress with the given ConfigFile.
/// ## Parameters
///
///  - `setParams` - Function used to overwrite the default parameters.
///  - `configFileName` - The file name of the IISExpress configfile.
///  - `siteId` - The id (in the config file) of the website to run.
///
/// ## Sample
///
///      HostWebsite (fun p -> { p with ToolPath = "iisexpress.exe" }) "configfile.config" 1
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
let HostWebsite setParams configFileName siteId = 
    let parameters = setParams IISExpressDefaults

    use __ = Trace.traceTask "StartWebSite" configFileName
    let args = sprintf "/config:\"%s\" /siteid:%d" configFileName siteId
    Trace.tracefn "Starting WebSite with %s %s" parameters.ToolPath args

    let proc = 
        ProcessStartInfo(FileName = parameters.ToolPath, Arguments = args, UseShellExecute = false) 
        |> Process.Start

    proc

let HostStaticWebsite setParams folder = 
    // https://blogs.msdn.microsoft.com/rido/2015/09/30/serving-static-content-with-iisexpress/
    let parameters = setParams IISExpressDefaults

    use __ = Trace.traceTask "StartWebSite" folder
    let args = sprintf @"/path:""%s\""" folder
    Trace.tracefn "Starting WebSite with %s %s" parameters.ToolPath args

    let proc = 
        ProcessStartInfo(FileName = parameters.ToolPath, Arguments = args, UseShellExecute = false) 
        |> Process.Start

    proc

/// Opens the given url in the browser
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
let OpenUrlInBrowser url = Process.Start(url:string) |> ignore

/// Opens the given website in the browser
[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
let OpenWebsiteInBrowser hostName port = sprintf "http://%s:%d/" hostName port |> OpenUrlInBrowser