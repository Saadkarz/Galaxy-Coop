using System.Net;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Server;

public sealed record ServerOptions
{
    public int MaxPlayers { get; init; } = 1 + MailboxLayout.MaxRemote;
    public string Password { get; init; } = "";
    public string GameId { get; init; } = "SB4E";
    public int TimeoutMs { get; init; } = 8000;
    public string RoomName { get; init; } = "Galaxy Co-op";
    /// <summary>Lobby to list this server on; null keeps it private (join by address only).</summary>
    public IPEndPoint? Lobby { get; init; }
    public int LobbyIntervalMs { get; init; } = 5000;
}

/// <summary>
/// Socket-free server core: feed it datagrams and ticks, it answers through
/// the transport. Relays snapshots unchanged (apart from stamping the sender
/// id), arbitrates world events per stage, and assigns the host, the player
/// whose galaxy choice others follow.
/// </summary>
public sealed class RelayServer
{
    public sealed class Peer(byte id, string name, IPEndPoint endPoint, long now)
    {
        public readonly byte Id = id;
        public string Name = name;
        public readonly IPEndPoint EndPoint = endPoint;
        public readonly ReliableChannel Channel = new();
        public long LastHeard = now;
        public string Stage = "";
        public int Scenario;
    }

    readonly ITransport _transport;
    readonly ServerOptions _options;
    readonly Dictionary<IPEndPoint, Peer> _byEndPoint = new();
    readonly SortedDictionary<byte, Peer> _byId = new();
    readonly Dictionary<(string, int), StageState> _stages = new();
    readonly uint _sessionId = (uint)Random.Shared.Next();
    long? _lastLobbyRegister;

    public byte? HostId { get; private set; }
    /// <summary>Address the lobby sees this server at, once it has answered a registration.</summary>
    public IPEndPoint? PublicEndPoint { get; private set; }
    public bool IsPublic => _options.Lobby is not null;
    public IReadOnlyCollection<Peer> Peers => _byId.Values;
    public event Action<string>? Log;

    public RelayServer(ITransport transport, ServerOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new ServerOptions();
    }

    public StageState? GetStage(string stage, int scenario) => _stages.GetValueOrDefault((stage, scenario));

    public void OnPacket(IPEndPoint from, ReadOnlySpan<byte> data, long nowMs)
    {
        var msg = Codec.Decode(data);
        if (msg is null) return;
        if (msg is Hello hello)
        {
            HandleHello(from, hello, nowMs);
            return;
        }
        if (msg is LobbyRegistered reg && from.Equals(_options.Lobby))
        {
            if (IPAddress.TryParse(reg.PublicAddress, out var ip))
            {
                var ep = new IPEndPoint(ip, reg.PublicPort);
                if (!ep.Equals(PublicEndPoint)) Log?.Invoke($"listed publicly; players can reach this server at {ep}");
                PublicEndPoint = ep;
            }
            return;
        }
        if (!_byEndPoint.TryGetValue(from, out var peer)) return;
        peer.LastHeard = nowMs;

        switch (msg)
        {
            case Boss b:
            {
                var bossRelay = Codec.Encode(b with { PlayerId = peer.Id });
                foreach (var other in _byId.Values)
                    if (other != peer) _transport.Send(other.EndPoint, bossRelay);
                break;
            }
            case Snapshot s:
                if (s.State.InScene)
                {
                    peer.Stage = s.State.Stage;
                    peer.Scenario = s.State.Scenario;
                }
                var relay = Codec.Encode(s with { PlayerId = peer.Id });
                foreach (var other in _byId.Values)
                    if (other != peer) _transport.Send(other.EndPoint, relay);
                break;
            case ReliablePacket p:
                foreach (var m in peer.Channel.Receive(p)) HandleReliable(peer, m, nowMs);
                _transport.Send(from, Codec.Encode(new Ack(new[] { p.Sequence })));
                break;
            case Ack a:
                peer.Channel.OnAck(a);
                break;
            case Ping ping:
                _transport.Send(from, Codec.Encode(new Pong(ping.TimeMs)));
                break;
            case Bye:
                Remove(peer, "left", nowMs);
                break;
        }
    }

    void HandleHello(IPEndPoint from, Hello h, long now)
    {
        if (_byEndPoint.TryGetValue(from, out var existing))
        {
            existing.LastHeard = now;
            _transport.Send(from, Codec.Encode(new Welcome(h.Nonce, existing.Id, HostId ?? existing.Id, _sessionId)));
            return;
        }

        string? reason = null;
        if (h.GameId != _options.GameId) reason = $"server runs {_options.GameId}, you run {h.GameId}";
        else if (_options.Password.Length > 0 && h.Password != _options.Password) reason = "wrong password";
        else if (_byId.Count >= _options.MaxPlayers) reason = "server full";
        if (reason is not null)
        {
            _transport.Send(from, Codec.Encode(new Reject(h.Nonce, reason)));
            Log?.Invoke($"rejected {h.Name} ({from}): {reason}");
            return;
        }

        byte id = 1;
        while (_byId.ContainsKey(id)) id++;
        var name = string.IsNullOrWhiteSpace(h.Name) ? $"Player{id}" : h.Name.Trim();
        var peer = new Peer(id, name, from, now);
        _byEndPoint[from] = peer;
        _byId[id] = peer;
        HostId ??= id;

        _transport.Send(from, Codec.Encode(new Welcome(h.Nonce, id, HostId.Value, _sessionId)));
        Log?.Invoke($"{name} joined as #{id} from {from}{(HostId == id ? " (host)" : "")}");

        foreach (var other in _byId.Values)
        {
            if (other == peer) continue;
            SendReliable(peer, new PlayerJoined(other.Id, other.Name), now);
            SendReliable(other, new PlayerJoined(peer.Id, peer.Name), now);
        }
        if (HostId != id && _byId[HostId.Value] is { Stage.Length: > 0 } host)
            SendReliable(peer, new Travel(host.Stage, host.Scenario), now);
    }

    void HandleReliable(Peer peer, ReliableMessage m, long now)
    {
        switch (m)
        {
            case WorldEvent { Applied: false } e:
            {
                var key = (e.Stage, e.Scenario);
                if (!_stages.TryGetValue(key, out var st)) _stages[key] = st = new StageState();
                if (!st.TryApply(peer.Id, e.Event)) break;
                var applied = new WorldEvent(peer.Id, e.Stage, e.Scenario, e.Event, true);
                foreach (var other in _byId.Values)
                    if (other != peer) SendReliable(other, applied, now);
                break;
            }
            case Chat c:
                foreach (var other in _byId.Values)
                    if (other != peer) SendReliable(other, new Chat(peer.Name, c.Text), now);
                break;
            case Rename r:
                peer.Name = string.IsNullOrWhiteSpace(r.Name) ? peer.Name : r.Name.Trim();
                Log?.Invoke($"#{peer.Id} is now {peer.Name}");
                foreach (var other in _byId.Values)
                    if (other != peer) SendReliable(other, new Rename(peer.Id, peer.Name), now);
                break;
            case StageEnter s:
            {
                var key = (s.Stage, s.Scenario);
                bool othersThere = _byId.Values.Any(o => o != peer && o.Stage == s.Stage && o.Scenario == s.Scenario);
                if (!othersThere) _stages.Remove(key);
                peer.Stage = s.Stage;
                peer.Scenario = s.Scenario;
                DropEmptyStages();

                if (_stages.TryGetValue(key, out var st))
                    foreach (var (src, ev) in st.Replay())
                        SendReliable(peer, new WorldEvent(src, s.Stage, s.Scenario, ev, true), now);

                Log?.Invoke($"#{peer.Id} {peer.Name} entered {s.Stage} scenario {s.Scenario}");
                if (peer.Id == HostId)
                    foreach (var other in _byId.Values)
                        if (other != peer) SendReliable(other, new Travel(s.Stage, s.Scenario), now);
                break;
            }
        }
    }

    void DropEmptyStages()
    {
        foreach (var key in _stages.Keys.ToList())
            if (!_byId.Values.Any(p => p.Stage == key.Item1 && p.Scenario == key.Item2))
                _stages.Remove(key);
    }

    public void Tick(long nowMs)
    {
        foreach (var peer in _byId.Values.ToList())
        {
            if (nowMs - peer.LastHeard > _options.TimeoutMs || peer.Channel.Failed)
            {
                Remove(peer, "timed out", nowMs);
                continue;
            }
            foreach (var packet in peer.Channel.CollectResends(nowMs))
                _transport.Send(peer.EndPoint, packet);
        }

        if (_options.Lobby is { } lobby && (_lastLobbyRegister is not long last || nowMs - last >= _options.LobbyIntervalMs))
        {
            _lastLobbyRegister = nowMs;
            _transport.Send(lobby, Codec.Encode(new LobbyRegister(_options.RoomName, _options.GameId,
                (byte)_byId.Count, (byte)_options.MaxPlayers, _options.Password.Length > 0)));
        }
    }

    /// <summary>Takes the room off the public list; call before closing the socket.</summary>
    public void Shutdown()
    {
        if (_options.Lobby is { } lobby) _transport.Send(lobby, Codec.Encode(new LobbyUnregister()));
    }

    void Remove(Peer peer, string reason, long now)
    {
        if (!_byId.Remove(peer.Id)) return;
        _byEndPoint.Remove(peer.EndPoint);
        Log?.Invoke($"#{peer.Id} {peer.Name} {reason}");
        foreach (var other in _byId.Values)
            SendReliable(other, new PlayerLeft(peer.Id), now);

        if (HostId == peer.Id)
        {
            HostId = _byId.Count > 0 ? _byId.Keys.First() : null;
            if (HostId is byte h)
            {
                Log?.Invoke($"host is now #{h} {_byId[h].Name}");
                foreach (var other in _byId.Values)
                    SendReliable(other, new HostChanged(h), now);
            }
        }
        DropEmptyStages();
    }

    void SendReliable(Peer peer, ReliableMessage m, long now) =>
        _transport.Send(peer.EndPoint, peer.Channel.Send(m, now));
}
