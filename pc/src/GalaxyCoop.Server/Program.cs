using System.Diagnostics;
using GalaxyCoop.Protocol.Net;
using GalaxyCoop.Server;

const string Usage = """
GalaxyCoop server
  [--port 7777] [--password X] [--max-players 8] [--timeout-ms 8000]
  [--public "Room name" --lobby HOST[:7700]]     list the room on a lobby
GalaxyCoop lobby (the public room list)
  --lobby-mode [--port 7700]
""";

int? port = null;
bool lobbyMode = false;
string? lobbyText = null;
var options = new ServerOptions();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--password": options = options with { Password = args[++i] }; break;
        case "--max-players": options = options with { MaxPlayers = int.Parse(args[++i]) }; break;
        case "--timeout-ms": options = options with { TimeoutMs = int.Parse(args[++i]) }; break;
        case "--public": options = options with { RoomName = args[++i] }; lobbyText ??= ""; break;
        case "--lobby": lobbyText = args[++i]; break;
        case "--lobby-mode": lobbyMode = true; break;
        case "-h":
        case "--help":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument {args[i]}\n{Usage}");
            return 2;
    }
}
if (lobbyText is "") 
{
    Console.Error.WriteLine("--public needs --lobby HOST[:PORT]");
    return 2;
}
if (lobbyText is not null)
    options = options with { Lobby = UdpTransport.ParseEndpoint(lobbyText, Codec.DefaultLobbyPort) };

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var udp = new UdpTransport(port ?? (lobbyMode ? Codec.DefaultLobbyPort : Codec.DefaultPort));
var clock = Stopwatch.StartNew();
var gate = new object();
void Log(string s) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s}");

Action<System.Net.IPEndPoint, byte[], long> onPacket;
Action<long> tick;
Action shutdown = () => { };
if (lobbyMode)
{
    var lobby = new LobbyServer(udp);
    lobby.Log += Log;
    onPacket = (from, data, now) => lobby.OnPacket(from, data, now);
    tick = lobby.Tick;
    Console.WriteLine($"GalaxyCoop lobby listening on UDP {udp.LocalEndPoint.Port}");
}
else
{
    var server = new RelayServer(udp, options);
    server.Log += Log;
    onPacket = (from, data, now) => server.OnPacket(from, data, now);
    tick = server.Tick;
    shutdown = server.Shutdown;
    Console.WriteLine($"GalaxyCoop server listening on UDP {udp.LocalEndPoint.Port} (max {options.MaxPlayers} players" +
                      $"{(options.Password.Length > 0 ? ", password set" : "")}" +
                      $"{(options.Lobby is { } l ? $", listed as '{options.RoomName}' on {l}" : ", private")})");
}

var ticker = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
    while (await timer.WaitForNextTickAsync(cts.Token))
        lock (gate) tick(clock.ElapsedMilliseconds);
});

try
{
    while (!cts.IsCancellationRequested)
    {
        var r = await udp.ReceiveAsync(cts.Token);
        if (r is not { } packet) continue;
        lock (gate) onPacket(packet.RemoteEndPoint, packet.Buffer, clock.ElapsedMilliseconds);
    }
}
catch (OperationCanceledException) { }
lock (gate) shutdown();
try { await ticker; } catch (OperationCanceledException) { }
return 0;
