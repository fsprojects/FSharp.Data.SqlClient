namespace FSharp.Data.SqlClient

open System
open System.IO

//[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
//type internal SingleFileChangeMonitor(path) as this = 
//    inherit ChangeMonitor()
//
//    let file = new FileInfo(path)
//    let watcher = new FileSystemWatcher( Path.GetDirectoryName(path) )
//
//    do
//        let dispose = ref true
//        try
//            watcher.NotifyFilter <- NotifyFilters.LastWrite ||| NotifyFilters.FileName
//            watcher.Changed.Add <| fun args -> this.TriggerOnFileChange(args.Name)
//            watcher.Deleted.Add <| fun args -> this.TriggerOnFileChange(args.Name)
//            watcher.Renamed.Add <| fun args -> this.TriggerOnFileChange(args.OldName)
//            watcher.Error.Add <| fun _ -> this.TriggerOnChange()
//            watcher.EnableRaisingEvents <- true
//            dispose := false
//        finally 
//            base.InitializationComplete()
//            if !dispose 
//            then 
//                base.Dispose()
//
//    member private __.TriggerOnChange() = base.OnChanged(state = null)
//    member private __.TriggerOnFileChange(fileName) = 
//        if String.Compare(file.Name, fileName, StringComparison.OrdinalIgnoreCase) = 0  
//        then 
//            this.TriggerOnChange()
//
//    override __.UniqueId = path + string file.LastWriteTimeUtc.Ticks + string file.Length;
//    override __.Dispose( disposing) = if disposing then watcher.Dispose()
//
