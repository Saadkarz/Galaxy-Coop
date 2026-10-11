using System.Net;
using System.Numerics;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Companion;

public enum ClientState { Connecting, Connected, Rejected }

public sealed class RemotePeer(byte id, string name, int slot)
{
    public readonly byte Id = id;
    public string Name = name;
    /// <summary>Mailbox remote slot this peer is drawn in, or -1 if all slots are taken.</summary>
    public readonly int Slot = slot;
    public readonly SnapshotInterpolator Interpolator = new();
    public long? LastSnapshotMs;
    public PlayerState? LastState;
}

/// <summary>
/// Socket-free client core. Connects (and reconnects) to a relay server, keeps
/// the remote player roster with one interpolator each, and queues the world
/// events and travel requests the server sends.
/// </summary>
public sealed class NetClient
{
    readonly ITransport _transport;
    readonly IPEndPoint _server;
    string _name;
    readonly string _password, _gameId;
    readonly Dictionary<byte, RemotePeer> _peers = new();
    ReliableChannel _channel = new();
    uint _nonce = (uint)Random.Shared.Next();
    long? _lastHello, _lastPing;
    long _lastHeard;
    uint _snapshotSeq;

    public ClientState State { get; private set; } = ClientState.Connecting;
    public byte PlayerId { get; private set; }
    public byte HostId { get; private set; }
    public bool IsConnected => State == ClientState.Connected;
    public string Name => _name;
    public bool IsHost => IsConnected && PlayerId == HostId;
    public int RttMs { get; private set; }
    public int TimeoutMs { get; init; } = 10000;
    public IReadOnlyCollection<RemotePeer> Peers => _peers.Values;
    public Queue<WorldEvent> IncomingEvents { get; } = new();
    public Travel? PendingTravel { get; set; }
    /// <summary>Latest boss pose reported by someone else in the session. The host's pose wins.</summary>
    public Boss? RemoteBoss { get; private set; }
    public List<Chat> ChatLog { get; } = new();

    public event Action<string>? Status;
    public event Action? Connected;
    public event Action<RemotePeer>? PeerRemoved;

    public NetClient(ITransport transport, IPEndPoint server, string name, string password = "", string gameId = "SB4E")
    {
        _transport = transport;
        _server = server;
        _name = name;
        _password = password;
        _gameId = gameId;
    }

    public void Tick(long nowMs)
    {
        switch (State)
        {
            case ClientState.Connecting:
                if (_lastHello is not long hello || nowMs - hello >= 500)
                {
                    _lastHello = nowMs;
                    Send(new Hello(_nonce, _name, _gameId, _password));
                }
                break;
            case ClientState.Connected:
                if (nowMs - _lastHeard > TimeoutMs || _channel.Failed)
                {
                    Status?.Invoke("lost connection to server; reconnecting");
                    Reset();
                    break;
                }
                if (_lastPing is not long ping || nowMs - ping >= 1000)
                {
                    _lastPing = nowMs;
                    Send(new Ping((uint)nowMs));
                }
                foreach (var p in _channel.CollectResends(nowMs)) _transport.Send(_server, p);
                break;
        }
    }

    void Reset()
    {
        foreach (var peer in _peers.Values.ToList()) RemovePeer(peer.Id);
        _channel = new ReliableChannel();
        _nonce = (uint)Random.Shared.Next();
        _lastHello = null;
        _lastPing = null;
        IncomingEvents.Clear();
        PendingTravel = null;
        RemoteBoss = null;
        ChatLog.Clear();
        State = ClientState.Connecting;
    }

    public void OnPacket(IPEndPoint from, ReadOnlySpan<byte> data, long nowMs)
    {
        if (!from.Equals(_server)) return;
        var msg = Codec.Decode(data);
        if (msg is null) return;
        _lastHeard = nowMs;

        switch (msg)
        {
            case Welcome w when State == ClientState.Connecting && w.Nonce == _nonce:
                State = ClientState.Connected;
                PlayerId = w.PlayerId;
                HostId = w.HostId;
                Status?.Invoke($"connected as player #{PlayerId}{(IsHost ? " (host)" : $", host is #{HostId}")}");
                Connected?.Invoke();
                break;
            case Reject r when State == ClientState.Connecting && r.Nonce == _nonce:
                State = ClientState.Rejected;
                Status?.Invoke($"server rejected us: {r.Reason}");
                break;
            case Boss b when IsConnected && b.PlayerId != PlayerId:
                if (RemoteBoss is null || b.PlayerId == HostId || RemoteBoss.PlayerId == b.PlayerId)
                    RemoteBoss = b;
                break;
            case Snapshot s when IsConnected && s.PlayerId != PlayerId:
            {
                var peer = GetOrAddPeer(s.PlayerId, $"Player{s.PlayerId}");
                peer.Interpolator.Add(s.SenderTimeMs, s.State, nowMs);
                peer.LastSnapshotMs = nowMs;
                peer.LastState = s.State;
                break;
            }
            case ReliablePacket p when IsConnected:
                foreach (var m in _channel.Receive(p)) Handle(m);
                Send(new Ack(new[] { p.Sequence }));
                break;
            case Ack a when IsConnected:
                _channel.OnAck(a);
                break;
            case Pong pong:
                RttMs = (int)((uint)nowMs - pong.TimeMs);
                break;
        }
    }

    void Handle(ReliableMessage m)
    {
        switch (m)
        {
            case PlayerJoined j:
                GetOrAddPeer(j.PlayerId, j.Name).Name = j.Name;
                Status?.Invoke($"{j.Name} (#{j.PlayerId}) joined");
                break;
            case PlayerLeft l:
                if (_peers.TryGetValue(l.PlayerId, out var gone)) Status?.Invoke($"{gone.Name} (#{l.PlayerId}) left");
                RemovePeer(l.PlayerId);
                break;
            case HostChanged h:
                HostId = h.PlayerId;
                Status?.Invoke(IsHost ? "you are now the host" : $"host is now #{h.PlayerId}");
                break;
            case Travel t:
                PendingTravel = t;
                break;
            case Chat c:
                ChatLog.Add(c);
                while (ChatLog.Count > MailboxLayout.ChatLines) ChatLog.RemoveAt(0);
                break;
            case Rename r:
                if (_peers.TryGetValue(r.PlayerId, out var renamed)) renamed.Name = r.Name;
                break;
            case WorldEvent { Applied: true } e:
                IncomingEvents.Enqueue(e);
                break;
        }
    }

    RemotePeer GetOrAddPeer(byte id, string name)
    {
        if (_peers.TryGetValue(id, out var peer)) return peer;
        int slot = Enumerable.Range(0, MailboxLayout.MaxRemote).Where(s => _peers.Values.All(p => p.Slot != s)).DefaultIfEmpty(-1).First();
        peer = new RemotePeer(id, name, slot);
        _peers[id] = peer;
        return peer;
    }

    void RemovePeer(byte id)
    {
        if (_peers.Remove(id, out var peer)) PeerRemoved?.Invoke(peer);
    }

    public void SendSnapshot(PlayerState state, long nowMs)
    {
        if (IsConnected) Send(new Snapshot(PlayerId, (uint)nowMs, ++_snapshotSeq, state with { PlayerId = PlayerId }));
    }

    public void SendEvent(string stage, int scenario, GameEvent ev, long nowMs) =>
        SendReliable(new WorldEvent(PlayerId, stage, scenario, ev with { Source = PlayerId }, false), nowMs);

    public void SendStageEnter(string stage, int scenario, long nowMs) =>
        SendReliable(new StageEnter(stage, scenario), nowMs);

    public void SendBoss(uint key, Vector3 pos, Vector3 rot, bool active)
    {
        if (IsConnected) Send(new Boss(PlayerId, key, pos, rot, active));
    }

    public void SendChat(string text, long nowMs)
    {
        if (text.Length == 0) return;
        SendReliable(new Chat("", text), nowMs);
    }

    public void Rename(string name, long nowMs)
    {
        if (name.Length == 0) return;
        _name = name;
        if (IsConnected) SendReliable(new Rename(PlayerId, name), nowMs);
    }

    void SendReliable(ReliableMessage m, long nowMs)
    {
        if (IsConnected) _transport.Send(_server, _channel.Send(m, nowMs));
    }

    public void Disconnect()
    {
        if (IsConnected) Send(new Bye());
    }

    void Send(Message m) => _transport.Send(_server, Codec.Encode(m));
}
