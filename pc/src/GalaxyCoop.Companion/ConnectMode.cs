using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Companion;

/// <summary>Bridges one game mailbox and one server connection.</summary>
public static class ConnectMode
{
    const int SnapshotIntervalMs = 33;
    const int StaleRemoteMs = 3000;

    public static async Task RunAsync(Options opts, CancellationToken ct)
    {
        var serverText = opts.Get("server") ?? throw new ArgumentException("connect needs --server HOST[:PORT]");
        var server = UdpTransport.ParseEndpoint(serverText, Codec.DefaultPort);
        var name = opts.Get("name") ?? Environment.UserName;
        bool followHost = !opts.Has("no-follow-host");
        var syncFlags = (opts.Has("sync-switches") ? CompanionFlags.SyncSwitch : 0)
                        | (opts.Has("sync-enemies") ? CompanionFlags.SyncEnemy : 0)
                        | (opts.Has("share-stars") ? CompanionFlags.ShareStars : 0)
                        | CharacterFlag(opts.Get("character"));

        using var udp = new UdpTransport();
        var client = new NetClient(udp, server, name, opts.Get("password") ?? "");
        using var link = new GameLink(opts.GetInt("pid"));
        var session = new Session(client, link, followHost, syncFlags);

        client.Status += s => Console.WriteLine($"[net] {s}");
        link.Status += s => Console.WriteLine($"[game] {s}");
        session.Log += s => Console.WriteLine($"[game] {s}");
        Console.WriteLine($"[net] connecting to {server} as '{name}'{(followHost ? "" : " (not following host)")}");

        var inbox = new ConcurrentQueue<UdpReceiveResultBox>();
        var receiver = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
                if (await udp.ReceiveAsync(ct) is { } r) inbox.Enqueue(new(r.RemoteEndPoint, r.Buffer));
        }, ct);

        var clock = Stopwatch.StartNew();
        long lastStatus = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long now = clock.ElapsedMilliseconds;
                while (inbox.TryDequeue(out var packet)) client.OnPacket(packet.From, packet.Data, now);
                client.Tick(now);
                session.Step(now);
                if (client.State == ClientState.Rejected) break;

                if (now - lastStatus > 5000)
                {
                    lastStatus = now;
                    Console.WriteLine($"[status] {(client.IsConnected ? $"online #{client.PlayerId}{(client.IsHost ? " host" : "")} rtt {client.RttMs} ms" : "offline")}, " +
                                      $"{client.Peers.Count} other player(s), game {(link.IsReady ? session.Where : "not attached")}");
                }
                await Task.Delay(8, ct);
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    readonly record struct UdpReceiveResultBox(IPEndPoint From, byte[] Data);

    /// <summary>"mario", "luigi" or nothing (keep whatever the save file says).</summary>
    public static CompanionFlags CharacterFlag(string? character) => character?.ToLowerInvariant() switch
    {
        null or "" or "save" => CompanionFlags.None,
        "mario" => CompanionFlags.ForceMario,
        "luigi" => CompanionFlags.ForceLuigi,
        _ => throw new ArgumentException($"--character must be mario or luigi, not '{character}'"),
    };

    /// <summary>
    /// Galaxies you can be sent to. The title screen, world maps, prologue and credits
    /// are stages too, but jumping into or out of them would skip save selection or story.
    /// </summary>
    public static bool IsPlayableStage(string stage) =>
        stage.EndsWith("Galaxy", StringComparison.Ordinal)
        && !stage.StartsWith("WorldMap", StringComparison.Ordinal)
        && stage is not ("PeachCastleGalaxy" or "StaffRollGalaxy");

    /// <summary>The opening story. A player who joins an existing session leaves it for the host's galaxy instead of watching it.</summary>
    public static bool IsIntroStage(string stage) =>
        stage is "PeachCastleGalaxy" || stage.StartsWith("Storybook", StringComparison.Ordinal);

    /// <summary>The per-frame glue, separated from sockets and timers so tests can drive it.</summary>
    public sealed class Session
    {
        readonly NetClient _client;
        readonly GameLink _link;
        readonly bool _followHost;
        readonly CompanionFlags _syncFlags;
        (string stage, int scenario, uint serial)? _announced;
        long? _lastSnapshot;
        readonly HashSet<int> _liveSlots = new();
        bool _bossWasActive;

        public string Stage { get; private set; } = "";
        public int Scenario { get; private set; }
        public string Where => Stage.Length > 0 ? $"{Stage} #{Scenario}" : "not in a galaxy";
        public PlayerState? Local { get; private set; }
        public event Action<string>? Log;
        /// <summary>Name offered to the game until the player types their own. The launcher keeps this in step with its settings.</summary>
        public string PresetName { get; set; } = "";

        public Session(NetClient client, GameLink link, bool followHost, CompanionFlags syncFlags)
        {
            _client = client;
            _link = link;
            _followHost = followHost;
            _syncFlags = syncFlags;
            client.Connected += () => _announced = null;
            client.PeerRemoved += p =>
            {
                if (p.Slot >= 0 && _link.IsReady) _link.Bridge!.ClearRemote(p.Slot);
                _liveSlots.Remove(p.Slot);
            };
            link.Attached += () =>
            {
                _announced = null;
                _liveSlots.Clear();
            };
        }

        public void Step(long now) => Step(now, _link.Poll());

        /// <summary>For owners that poll the link themselves (each poll consumes the game's outgoing events).</summary>
        public void Step(long now, MailboxSnapshot? snap)
        {
            if (snap is null || !_link.IsReady) return;
            var bridge = _link.Bridge!;

            var flags = CompanionFlags.Attached | _syncFlags | (_client.IsConnected ? CompanionFlags.Connected : 0);
            bridge.WriteHeartbeat(flags);

            Local = snap.Local;
            if (snap.Local is { InScene: true } local)
            {
                Stage = local.Stage;
                Scenario = local.Scenario;
                var here = (local.Stage, local.Scenario, snap.SceneSerial);
                if (_client.IsConnected && _announced != here)
                {
                    _announced = here;
                    _client.SendStageEnter(local.Stage, local.Scenario, now);
                }
                if (_lastSnapshot is not long last || now - last >= SnapshotIntervalMs)
                {
                    _lastSnapshot = now;
                    _client.SendSnapshot(local, now);
                }
            }

            foreach (var ev in snap.OutgoingEvents)
                if (Stage.Length > 0) _client.SendEvent(Stage, Scenario, ev, now);

            while (_client.IncomingEvents.TryPeek(out var incoming))
            {
                if (incoming.Event.Type == GameEventType.PlayerDeath)
                {
                    var who = _client.Peers.FirstOrDefault(p => p.Id == incoming.SourcePlayer)?.Name ?? $"player #{incoming.SourcePlayer}";
                    Log?.Invoke($"{who} lost a life in {incoming.Stage}");
                    _client.IncomingEvents.Dequeue();
                    continue;
                }
                if (incoming.Stage == Stage && incoming.Scenario == Scenario
                    && !bridge.PushIncomingEvent(incoming.Event with { Source = incoming.SourcePlayer }))
                    break;
                _client.IncomingEvents.Dequeue();
            }

            if (_client.PendingTravel is { } travel && _followHost && !_client.IsHost)
            {
                if (!IsPlayableStage(travel.Stage))
                {
                    _client.PendingTravel = null;
                }
                else if (IsPlayableStage(Stage) || IsIntroStage(Stage))
                {
                    if (travel.Stage != Stage || travel.Scenario != Scenario)
                    {
                        Log?.Invoke($"following host to {travel.Stage} #{travel.Scenario}");
                        bridge.RequestTravel(travel.Stage, travel.Scenario);
                    }
                    _client.PendingTravel = null;
                }
            }

            var boss = bridge.ReadBoss();
            if (boss.Active || _bossWasActive) _client.SendBoss(boss.Key, boss.Pos, boss.Rot, boss.Active);
            _bossWasActive = boss.Active;
            /* The host's pose wins. Anyone not fighting follows whoever is. */
            var remoteBoss = _client.RemoteBoss;
            var bossIn = default(MailboxBridge.BossOut);
            if (!_client.IsHost && remoteBoss is { Active: true, PlayerId: var id } hosted && id == _client.HostId)
                bossIn = new MailboxBridge.BossOut(hosted.Key, hosted.Pos, hosted.Rot, true);
            else if (!boss.Active && remoteBoss is { Active: true } other)
                bossIn = new MailboxBridge.BossOut(other.Key, other.Pos, other.Rot, true);
            bridge.WriteBossIn(bossIn);

            var names = new string?[MailboxLayout.MaxRemote];
            foreach (var peer in _client.Peers)
                if (peer.Slot >= 0 && peer.Slot < names.Length) names[peer.Slot] = peer.Name;
            bridge.WriteSocialIn(PresetName.Length > 0 ? PresetName : _client.Name, _client.ChatLog, names);

            foreach (var peer in _client.Peers)
            {
                if (peer.Slot < 0) continue;
                var state = peer.LastSnapshotMs is long seen && now - seen < StaleRemoteMs ? peer.Interpolator.Sample(now) : null;
                if (state is not null)
                {
                    bridge.WriteRemote(peer.Slot, state with { Active = true, PlayerId = peer.Id });
                    _liveSlots.Add(peer.Slot);
                }
                else if (_liveSlots.Remove(peer.Slot))
                {
                    bridge.ClearRemote(peer.Slot);
                }
            }
        }
    }
}
