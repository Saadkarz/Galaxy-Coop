using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Launcher;

/// <summary>
/// The launcher UI: a page served on http://localhost plus a small JSON API.
/// A browser page instead of a desktop window because Smart App Control blocks
/// unsigned native UI libraries, while this needs nothing beyond .NET itself.
/// </summary>
public sealed class WebUi(LauncherSettingsStore store)
{
    readonly SemaphoreSlim _sessionLock = new(1, 1);
    PlaySession? _session;

    public async Task RunAsync(HttpListener listener, CancellationToken ct)
    {
        using var reg = ct.Register(listener.Stop);
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            _ = Task.Run(() => HandleAsync(ctx, ct), ct);
        }
        await StopSessionAsync();
    }

    async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            // Only pages served by this launcher may drive it.
            if (req.HttpMethod == "POST" && req.Headers["Origin"] is { } origin && new Uri(origin).Host != "localhost")
            {
                await WriteJson(res, 403, new { error = "forbidden" });
                return;
            }

            var path = req.Url!.AbsolutePath;
            switch (req.HttpMethod, path)
            {
                case ("GET", "/"):
                    await WriteStatic(res, "index.html", "text/html; charset=utf-8");
                    break;
                case ("GET", "/app.css"):
                    await WriteStatic(res, "app.css", "text/css; charset=utf-8");
                    break;
                case ("GET", "/app.js"):
                    await WriteStatic(res, "app.js", "text/javascript; charset=utf-8");
                    break;
                case ("GET", "/api/state"):
                    await WriteJson(res, 200, State());
                    break;
                case ("POST", "/api/settings"):
                    store.Update(await ReadJson<LauncherSettings>(req));
                    await WriteJson(res, 200, State());
                    break;
                case ("GET", "/api/rooms"):
                {
                    var (rooms, error) = await LobbyBrowser.QueryAsync(store.Current.Lobby, "SB4E", ct);
                    await WriteJson(res, 200, new { lobby = store.Current.Lobby, rooms, error });
                    break;
                }
                case ("POST", "/api/host"):
                {
                    var r = await ReadJson<HostRequest>(req);
                    await StartSessionAsync(env => PlaySession.Host(store.Current, env, r));
                    store.Update(store.Current with { RoomName = r.RoomName, MaxPlayers = r.MaxPlayers, Port = r.Port });
                    await WriteJson(res, 200, State());
                    break;
                }
                case ("POST", "/api/join"):
                {
                    var r = await ReadJson<JoinRequest>(req);
                    await StartSessionAsync(env => PlaySession.Join(store.Current, env, r));
                    store.Update(store.Current with { LastJoinAddress = r.Address });
                    await WriteJson(res, 200, State());
                    break;
                }
                case ("POST", "/api/stop"):
                    await StopSessionAsync();
                    await WriteJson(res, 200, State());
                    break;
                case ("POST", "/api/dolphin"):
                    (_session ?? throw new InvalidOperationException("not in a session")).LaunchDolphin();
                    await WriteJson(res, 200, State());
                    break;
                default:
                    await WriteJson(res, 404, new { error = $"no route {req.HttpMethod} {path}" });
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException
                                       or JsonException or System.Net.Sockets.SocketException or IOException)
        {
            await WriteJson(res, 400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            await WriteJson(res, 500, new { error = ex.Message });
        }
    }

    object State()
    {
        var env = DolphinSetup.Inspect(store.Current);
        return new
        {
            settings = store.Current,
            environment = new { env.DolphinDir, env.GameImage, env.UserDir, patchReady = env.PatchDir is not null, env.Problems },
            session = _session?.GetView(),
            defaults = new { port = Codec.DefaultPort, lobbyPort = Codec.DefaultLobbyPort },
        };
    }

    async Task StartSessionAsync(Func<DolphinSetup.Environment, PlaySession> start)
    {
        await _sessionLock.WaitAsync();
        try
        {
            if (_session is not null) throw new InvalidOperationException("Leave the current session first.");
            var env = DolphinSetup.Inspect(store.Current);
            if (store.Current.LaunchDolphin && !env.Ready) throw new InvalidOperationException(string.Join(" ", env.Problems));
            _session = start(env);
        }
        finally { _sessionLock.Release(); }
    }

    async Task StopSessionAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            if (_session is { } s)
            {
                _session = null;
                await s.DisposeAsync();
            }
        }
        finally { _sessionLock.Release(); }
    }

    static async Task<T> ReadJson<T>(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(await reader.ReadToEndAsync(), LauncherSettings.Json)
               ?? throw new JsonException("empty request");
    }

    static async Task WriteJson(HttpListenerResponse res, int status, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, LauncherSettings.Json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    static async Task WriteStatic(HttpListenerResponse res, string name, string contentType)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wwwroot/" + name)
                           ?? throw new FileNotFoundException(name);
        res.ContentType = contentType;
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = stream.Length;
        await stream.CopyToAsync(res.OutputStream);
        res.Close();
    }
}

/// <summary>The current settings, saved to disk whenever the UI changes them.</summary>
public sealed class LauncherSettingsStore(string path)
{
    readonly object _gate = new();
    LauncherSettings _current = LauncherSettings.Load(path);

    public LauncherSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public void Update(LauncherSettings next)
    {
        lock (_gate)
        {
            _current = next with
            {
                Name = string.IsNullOrWhiteSpace(next.Name) ? Environment.UserName : next.Name.Trim()[..Math.Min(next.Name.Trim().Length, 24)],
                Character = next.Character is "luigi" ? "luigi" : "mario",
            };
            _current.Save(path);
        }
    }
}
