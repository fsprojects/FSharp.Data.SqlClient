#r @"bin\Debug\Lib.dll"
DataAccess.get42()

let fsi = System.Diagnostics.Process.GetCurrentProcess()
//fsi.StartInfo.EnvironmentVariables |> Seq.cast<System.Collections.DictionaryEntry> |> Seq.map (fun x -> x.Key, x.Value) |> Seq.toList

//fsi.StartInfo.WorkingDirectory

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__