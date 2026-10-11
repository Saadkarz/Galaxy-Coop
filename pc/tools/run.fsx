// Runs the Galaxy Co-op PC programs without letting Windows map the locally
// built DLLs as images. Smart App Control judges unsigned DLLs one build at a
// time and can block any of them after a rebuild; dotnet fsi is Microsoft
// signed and loading assemblies from byte arrays is not subject to that check.
//
//   dotnet fsi pc/tools/run.fsx launcher [--ui-port N] [--no-browser]
//   dotnet fsi pc/tools/run.fsx companion [args...]
//   dotnet fsi pc/tools/run.fsx server [args...]
//   dotnet fsi pc/tools/run.fsx test [name filter]
//
// Build first (dotnet build pc/GalaxyCoop.sln -c Debug).
open System
open System.IO
open System.Reflection
open System.Runtime.Loader
open System.Threading.Tasks

let pcDir = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let config = Environment.GetEnvironmentVariable "GCOOP_CONFIG" |> Option.ofObj |> Option.defaultValue "Debug"

let binOf (project: string) (folder: string) =
    Path.Combine(pcDir, folder, project, "bin", config, "net8.0", project + ".dll")

let loadFromBytes (path: string) =
    if not (File.Exists path) then failwithf "%s not found; run: dotnet build pc/GalaxyCoop.sln -c %s" path config
    let dir = Path.GetDirectoryName path
    AssemblyLoadContext.Default.add_Resolving(fun ctx name ->
        let candidate = Path.Combine(dir, name.Name + ".dll")
        if File.Exists candidate then ctx.LoadFromStream(new MemoryStream(File.ReadAllBytes candidate)) else null)
    AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(File.ReadAllBytes path))

let runMain (path: string) (args: string[]) : int =
    let asm = loadFromBytes path
    let entry = asm.EntryPoint
    let parameters = if entry.GetParameters().Length = 0 then null else [| box args |]
    try
        match entry.Invoke(null, parameters) with
        | :? int as code -> code
        | :? Task<int> as t -> t.GetAwaiter().GetResult()
        | :? Task as t -> t.GetAwaiter().GetResult(); 0
        | _ -> 0
    with :? TargetInvocationException as ex when not (isNull ex.InnerException) ->
        eprintfn "%O" ex.InnerException
        1

let argv = fsi.CommandLineArgs |> Array.skip 1
let rest = if argv.Length > 1 then argv[1..] else [||]
let code =
    match argv |> Array.tryHead with
    | Some "companion" -> runMain (binOf "GalaxyCoop.Companion" "src") rest
    | Some "server" -> runMain (binOf "GalaxyCoop.Server" "src") rest
    | Some "launcher" ->
        Environment.SetEnvironmentVariable("GCOOP_ROOT", Path.GetFullPath(Path.Combine(pcDir, "..")))
        runMain (binOf "GalaxyCoop.Launcher" "src") rest
    | Some "test" ->
        let tests = binOf "GalaxyCoop.Tests" "tests"
        runMain (binOf "GalaxyCoop.TestRunner" "tools") (Array.append [| tests |] rest)
    | _ ->
        eprintfn "usage: dotnet fsi pc/tools/run.fsx (launcher|companion|server|test) [args...]"
        2
exit code
