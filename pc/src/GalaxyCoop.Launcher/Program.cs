using System.Diagnostics;
using System.Net;
using GalaxyCoop.Launcher;

const string Usage = """
GalaxyCoop launcher: starts Dolphin with the Galaxy Co-op patch and serves the
in-game co-op menu (press - in a galaxy): pick Mario or Luigi, host a private
or public game, or join one.

  [--settings FILE]      settings file (default %APPDATA%\GalaxyCoop\launcher.json)
  [--dolphin-user DIR]   separate Dolphin user folder (for a second player on the same PC)
  [--web]                use the old browser page instead of the in-game menu
  [--ui-port 7790]       where the browser page is served (next free port if taken)
  [--no-browser]         with --web: don't open the page automatically
""";

int uiPort = 7790;
string settingsPath = LauncherSettings.DefaultPath;
bool openBrowser = true;
bool web = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ui-port": uiPort = int.Parse(args[++i]); break;
        case "--settings": settingsPath = Path.GetFullPath(args[++i]); break;
        case "--no-browser": openBrowser = false; break;
        case "--web": web = true; break;
        case "--dolphin-user": DolphinSetup.UserDirOverride = Path.GetFullPath(args[++i]); break;
        case "-h":
        case "--help":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument {args[i]}\n{Usage}");
            return 2;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var store = new LauncherSettingsStore(settingsPath);

if (!web)
{
    InGameLauncher launcher;
    try
    {
        launcher = InGameLauncher.Start(store);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"can't start: {ex.Message}");
        Console.Error.WriteLine($"Fix the paths in {settingsPath} (dolphinDir, gameImage) or run with --web to set them up.");
        return 1;
    }
    await using (launcher)
    {
        launcher.Log += s => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {s}");
        Console.WriteLine($"Galaxy Co-op: {(launcher.DolphinPid is { } pid ? $"started Dolphin (pid {pid})" : "waiting for Dolphin")}  (settings: {settingsPath})");
        Console.WriteLine("Enter a galaxy and use the co-op menu (it opens by itself, then press -). Keep this window open; Ctrl+C quits.");
        await launcher.RunAsync(cts.Token);
    }
    return 0;
}

HttpListener? listener = null;
string url = "";
for (int port = uiPort; port < uiPort + 20 && listener is null; port++)
{
    var candidate = new HttpListener();
    candidate.Prefixes.Add($"http://localhost:{port}/");
    try
    {
        candidate.Start();
        listener = candidate;
        url = $"http://localhost:{port}/";
    }
    catch (HttpListenerException)
    {
        candidate.Close();
    }
}
if (listener is null)
{
    Console.Error.WriteLine($"no free port for the launcher page between {uiPort} and {uiPort + 19}");
    return 1;
}

Console.WriteLine($"Galaxy Co-op launcher: {url}  (settings: {settingsPath})");
Console.WriteLine("Keep this window open while you play; Ctrl+C quits.");
if (openBrowser) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

var ui = new WebUi(store);
await ui.RunAsync(listener, cts.Token);
return 0;
