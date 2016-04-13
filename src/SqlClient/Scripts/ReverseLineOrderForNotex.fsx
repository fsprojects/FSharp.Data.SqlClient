
open System.IO

let releaseNotesfile = 
    [| __SOURCE_DIRECTORY__; "..\..\.."; "RELEASE_NOTES.md" |]
    |> Path.Combine    
    |> Path.GetFullPath

let release xs = 
    let isStart(s: string) = s.StartsWith("####")

    match xs with
    | h :: t when isStart h -> 
        let current = t |> List.takeWhile (not << isStart)
        let upcoming = t |> List.skipWhile (not << isStart)
        Some(h::current, upcoming)
    | _ -> None

let splitUpByRelease =
    releaseNotesfile 
    |> File.ReadAllLines 
    |> Array.toList
    |> Array.unfold release

let reversed = splitUpByRelease |> Array.rev |> Array.collect Array.ofList
    

File.WriteAllLines(releaseNotesfile, reversed)

