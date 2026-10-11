using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;
using GalaxyCoop.Server;

namespace GalaxyCoop.Launcher;

public sealed record HostRequest(string Visibility, string RoomName, string Password, int MaxPlayers, int Port);
public sealed record JoinRequest(string Address, string Password, string? RoomName = null);

/// <summary>
/// One play session: optionally a server on this PC (private or listed on the
/// lobby), plus the client that links the game to it. Launched from the web
/// page, it also starts Dolphin and runs its own loop; launched from the
/// in-game menu, it uses the caller's <see cref="GameLink"/> and the caller
/// drives it through <see cref="Pump"/>. <see cref="View"/> is what the UIs show.
/// </summary>
public sealed class PlaySession : IAsyncDisposable
{
    public sealed record LogLine(string Time, string Source, string Text);
    public sealed record PlayerView(byte Id, string Name, bool IsHost, bool IsYou, string Character, string Where);
    public sealed record View(
        string Role, string Visibility, string RoomName, bool HasPassword, string Status, string StatusText,
        string? LanAddress, string? PublicAddress, string? PortNote, bool DolphinRunning, bool GameAttached,
        string Where, PlayerView[] Players, LogLine[] Log);

    const int LoopMs = 8;
    const int MaxLog = 200;

    readonly object _gate = new();
    readonly CancellationTokenSource _cts = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<LogLine> _log = new();
    readonly List<Task> _tasks = new();
    readonly LauncherSettings _settings;
    readonly DolphinSetup.Environment _env;
    readonly string _role, _visibility, _roomName;
    readonly bool _hasPassword;
    readonly int _port;

    UdpTransport? _serverUdp;
    RelayServer? _server;
    UpnpPortMapper? _upnp;
    string? _portNote;
    UdpTransport? _clientUdp;
    NetClient? _client;
    GameLink? _link;
    ConnectMode.Session? _session;
    Process? _dolphin;
    readonly GameLink? _sharedLink;
    readonly CompanionFlags? _flags;
    readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> _inbox = new();

    PlaySession(LauncherSettings settings, DolphinSetup.Environment env, string role, string visibility, string roomName, bool hasPassword, int port,
        GameLink? sharedLink, CompanionFlags? flags)
    {
        _settings = settings;
        _env = env;
        _role = role;
        _visibility = visibility;
        _roomName = roomName;
        _hasPassword = hasPassword;
        _port = port;
        _sharedLink = sharedLink;
        _flags = flags;
    }

    public string Role => _role;
    public string RoomName => _roomName;
    /// <summary>The client's latest status message (for example why the server turned us away).</summary>
    public string LastNetStatus { get; private set; } = "";

    /// <summary>Name the game offers until the player types one.</summary>
    public string PresetName
    {
        get => _session?.PresetName ?? _settings.Name;
        set { if (_session is not null) _session.PresetName = value; }
    }

    public void Rename(string name)
    {
        lock (_gate) _client?.Rename(name, _clock.ElapsedMilliseconds);
    }

    public void SendChat(string text)
    {
        lock (_gate) _client?.SendChat(text, _clock.ElapsedMilliseconds);
    }

    public static PlaySession Host(LauncherSettings settings, DolphinSetup.Environment env, HostRequest r,
        GameLink? sharedLink = null, CompanionFlags? flags = null)
    {
        bool isPublic = r.Visibility == "public";
        int port = r.Port is > 0 and < 65536 ? r.Port : Codec.DefaultPort;
        var roomName = string.IsNullOrWhiteSpace(r.RoomName) ? settings.EffectiveRoomName : r.RoomName.Trim();
        var session = new PlaySession(settings, env, "host", isPublic ? "public" : "private", roomName, r.Password.Length > 0, port, sharedLink, flags);
        try
        {
            session.StartServer(new ServerOptions
            {
                MaxPlayers = Math.Clamp(r.MaxPlayers, 2, 1 + MailboxLayout.MaxRemote),
                Password = r.Password,
                RoomName = roomName,
                Lobby = isPublic ? UdpTransport.ParseEndpoint(settings.Lobby, Codec.DefaultLobbyPort) : null,
            });
            session.StartClient(new IPEndPoint(IPAddress.Loopback, port), r.Password);
        }
        catch
        {
            _ = session.DisposeAsync();
            throw;
        }
        return session;
    }

    public static PlaySession Join(LauncherSettings settings, DolphinSetup.Environment env, JoinRequest r,
        GameLink? sharedLink = null, CompanionFlags? flags = null)
    {
        var server = UdpTransport.ParseEndpoint(r.Address.Trim(), Codec.DefaultPort);
        var name = string.IsNullOrWhiteSpace(r.RoomName) ? r.Address.Trim() : r.RoomName.Trim();
        var session = new PlaySession(settings, env, "join", "", name, r.Password.Length > 0, server.Port, sharedLink, flags);
        try
        {
            session.StartClient(server, r.Password);
        }
        catch
        {
            _ = session.DisposeAsync();
            throw;
        }
        return session;
    }

    void AddLog(string source, string text)
    {
        lock (_log)
        {
            _log.Add(new LogLine(DateTime.Now.ToString("HH:mm:ss"), source, text));
            if (_log.Count > MaxLog) _log.RemoveAt(0);
        }
    }

    void StartServer(ServerOptions options)
    {
        try
        {
            _serverUdp = new UdpTransport(_port);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"UDP port {_port} is in use ({ex.Message}). Pick another port.");
        }
        _server = new RelayServer(_serverUdp, options);
        _server.Log += s => AddLog("server", s);
        AddLog("server", $"hosting '{options.RoomName}' on UDP {_port}, {(options.Lobby is { } l ? $"public on lobby {l}" : "private")}");

        var ct = _cts.Token;
        var serverGate = new object();
        _tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (await _serverUdp.ReceiveAsync(ct) is not { } packet) continue;
                lock (serverGate) _server.OnPacket(packet.RemoteEndPoint, packet.Buffer, _clock.ElapsedMilliseconds);
            }
        }, ct));
        _tasks.Add(Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(ct))
                lock (serverGate) _server.Tick(_clock.ElapsedMilliseconds);
        }, ct));
        _tasks.Add(Task.Run(async () =>
        {
            var (mapper, message) = await UpnpPortMapper.TryMapAsync(_port, $"Galaxy Co-op {_port}", ct);
            _upnp = mapper;
            _portNote = message;
            AddLog("network", message);
        }, ct));
    }

    void StartClient(IPEndPoint server, string password)
    {
        _clientUdp = new UdpTransport();
        _client = new NetClient(_clientUdp, server, _settings.Name, password);
        _client.Status += s =>
        {
            LastNetStatus = s;
            AddLog("net", s);
        };
        AddLog("net", $"connecting to {server} as {_settings.Name}");

        if (_sharedLink is not null) UseLink(_sharedLink);
        else if (_settings.LaunchDolphin) LaunchDolphin();
        else AttachGame(null);

        var ct = _cts.Token;
        _tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
                if (await _clientUdp.ReceiveAsync(ct) is { } r) _inbox.Enqueue((r.RemoteEndPoint, r.Buffer));
        }, ct));
        if (_sharedLink is not null) return;
        _tasks.Add(Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                lock (_gate) Advance(null, poll: true);
                await Task.Delay(LoopMs, ct);
            }
        }, ct));
    }

    /// <summary>One step of a session on a shared link, with the snapshot the owner just polled.</summary>
    public void Pump(MailboxSnapshot? snap)
    {
        lock (_gate) Advance(snap, poll: false);
    }

    void Advance(MailboxSnapshot? snap, bool poll)
    {
        if (_client is null) return;
        long now = _clock.ElapsedMilliseconds;
        while (_inbox.TryDequeue(out var p)) _client.OnPacket(p.from, p.data, now);
        _client.Tick(now);
        if (poll) _session?.Step(now);
        else _session?.Step(now, snap);
    }

    /// <summary>Starts (or restarts) Dolphin and links the companion to that instance.</summary>
    public void LaunchDolphin()
    {
        lock (_gate)
        {
            if (_sharedLink is not null) throw new InvalidOperationException("this session belongs to a running game");
            if (_dolphin is { HasExited: false })
            {
                AddLog("game", "Dolphin is already running");
                return;
            }
            var process = DolphinSetup.Launch(_env);
            _dolphin = process;
            AddLog("game", $"started Dolphin (pid {process.Id}) with the Galaxy Co-op patch");
            AttachGame(process.Id);
        }
    }

    void AttachGame(int? pid)
    {
        _link?.Dispose();
        _link = new GameLink(pid);
        _link.Status += s => AddLog("game", s);
        UseLink(_link);
    }

    void UseLink(GameLink link)
    {
        var flags = _flags ?? (ConnectMode.CharacterFlag(_settings.Character)
                               | (_settings.ShareStars ? CompanionFlags.ShareStars : 0)
                               | (_settings.SyncEnemies ? CompanionFlags.SyncEnemy : 0)
                               | (_settings.SyncSwitches ? CompanionFlags.SyncSwitch : 0));
        _session = new ConnectMode.Session(_client!, link, followHost: true, flags);
        _session.Log += s => AddLog("game", s);
    }

    public View GetView()
    {
        lock (_gate)
        {
            var c = _client!;
            string status = c.State switch
            {
                ClientState.Connected => "online",
                ClientState.Rejected => "rejected",
                _ => "connecting",
            };
            string statusText = c.State switch
            {
                ClientState.Connected => c.IsHost ? $"Online as host, ping {c.RttMs} ms" : $"Online, ping {c.RttMs} ms",
                ClientState.Rejected => "The server turned us away (see the log)",
                _ => _role == "host" ? "Starting the server..." : $"Looking for {_roomName}...",
            };

            var players = new List<PlayerView>();
            if (c.IsConnected)
            {
                var local = _session?.Local;
                players.Add(new PlayerView(c.PlayerId, _settings.Name, c.IsHost, true,
                    local is { } l ? CharacterName(l) : Capitalize(_settings.Character), _session?.Where ?? ""));
                foreach (var p in c.Peers.OrderBy(p => p.Id))
                    players.Add(new PlayerView(p.Id, p.Name, p.Id == c.HostId, false,
                        p.LastState is { } s ? CharacterName(s) : "?",
                        p.LastState is { InScene: true } st ? $"{st.Stage} #{st.Scenario}" : "not in a galaxy"));
            }

            string? lan = _role == "host" ? $"{LanAddress()}:{_port}" : null;
            string? pub = null;
            if (_role == "host")
            {
                if (_server?.PublicEndPoint is { } seen && IsInternetAddress(seen.Address)) pub = seen.ToString();
                else if (_upnp?.ExternalAddress is { } ip && IsInternetAddress(ip)) pub = $"{ip}:{_port}";
            }

            LogLine[] log;
            lock (_log) log = _log.TakeLast(80).ToArray();

            var link = _sharedLink ?? _link;
            return new View(_role, _visibility, _roomName, _hasPassword, status, statusText, lan, pub, _portNote,
                _sharedLink is not null || _dolphin is { HasExited: false }, link?.IsReady ?? false, _session?.Where ?? "", players.ToArray(), log);
        }
    }

    static string CharacterName(PlayerState s) => s.Character == 1 ? "Luigi" : "Mario";
    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>False for loopback, private (10/8, 172.16/12, 192.168/16), CGNAT (100.64/10) and link-local addresses.</summary>
    static bool IsInternetAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 0
                 || (b[0] == 172 && b[1] is >= 16 and <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 100 && b[1] is >= 64 and <= 127)
                 || (b[0] == 169 && b[1] == 254));
    }

    static IPAddress LanAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            if (props.GatewayAddresses.All(g => g.Address.AddressFamily != AddressFamily.InterNetwork)) continue;
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (ip is not null) return ip;
        }
        return IPAddress.Loopback;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _client?.Disconnect();
            _link?.Dispose();
            _link = null;
            _session = null;
            if (_sharedLink is { IsReady: true }) _sharedLink.Bridge!.ClearAllRemotes();
        }
        _server?.Shutdown();
        _cts.Cancel();
        try { await Task.WhenAll(_tasks); } catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        _serverUdp?.Dispose();
        _clientUdp?.Dispose();
        if (_upnp is not null) await _upnp.DisposeAsync();
    }
}

public static class LobbyBrowser
{
    /// <summary>Asks the lobby for open rooms; throws on a bad address, returns an error string when it does not answer.</summary>
    public static async Task<(LobbyRoom[] rooms, string? error)> QueryAsync(string lobbyText, string gameId, CancellationToken ct)
    {
        var lobby = UdpTransport.ParseEndpoint(lobbyText, Codec.DefaultLobbyPort);
        using var udp = new UdpTransport();
        uint nonce = (uint)Random.Shared.Next();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                udp.Send(lobby, Codec.Encode(new LobbyQuery(nonce, gameId)));
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                wait.CancelAfter(TimeSpan.FromMilliseconds(600));
                try
                {
                    while (true)
                    {
                        if (await udp.ReceiveAsync(wait.Token) is { } r && r.RemoteEndPoint.Equals(lobby)
                            && Codec.Decode(r.Buffer) is LobbyList list && list.Nonce == nonce)
                            return (list.Rooms, null);
                    }
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return (Array.Empty<LobbyRoom>(), $"the lobby at {lobby} did not answer");
    }
}
