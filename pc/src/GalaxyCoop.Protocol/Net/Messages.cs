using System.Numerics;

namespace GalaxyCoop.Protocol.Net;

public enum MessageType : byte
{
    Hello = 1,
    Welcome = 2,
    Reject = 3,
    Snapshot = 4,
    Reliable = 5,
    Ack = 6,
    Ping = 7,
    Pong = 8,
    Bye = 9,
    Boss = 10,

    // Public room list. A public server registers with the lobby from its own
    // socket, so the lobby sees (and lists) the address players can reach it on.
    LobbyRegister = 20,
    LobbyRegistered = 21,
    LobbyUnregister = 22,
    LobbyQuery = 23,
    LobbyList = 24,
}

/// <summary>Payload kinds carried inside Reliable packets (delivered in order, exactly once).</summary>
public enum ReliableType : byte
{
    EventRequest = 1,
    EventApplied = 2,
    PlayerJoined = 3,
    PlayerLeft = 4,
    HostChanged = 5,
    Travel = 6,
    StageEnter = 7,
    Chat = 8,
    Rename = 9,
}

public abstract record Message;
public sealed record Hello(uint Nonce, string Name, string GameId, string Password) : Message;
public sealed record Welcome(uint Nonce, byte PlayerId, byte HostId, uint SessionId) : Message;
public sealed record Reject(uint Nonce, string Reason) : Message;
public sealed record Snapshot(byte PlayerId, uint SenderTimeMs, uint Sequence, PlayerState State) : Message;
public sealed record ReliablePacket(uint Sequence, byte[] Payload) : Message;
public sealed record Ack(uint[] Sequences) : Message;
public sealed record Ping(uint TimeMs) : Message;
public sealed record Pong(uint TimeMs) : Message;
public sealed record Bye : Message;
/// <summary>Unreliable pose of the boss the sender is fighting. The receiver copies it onto its own boss and walks the other players over.</summary>
public sealed record Boss(byte PlayerId, uint Key, Vector3 Pos, Vector3 Rot, bool Active) : Message;

public sealed record LobbyRegister(string RoomName, string GameId, byte Players, byte MaxPlayers, bool HasPassword) : Message;
/// <summary>The lobby's answer to a registration: the address it saw, i.e. the host's public address.</summary>
public sealed record LobbyRegistered(string PublicAddress, ushort PublicPort) : Message;
public sealed record LobbyUnregister : Message;
public sealed record LobbyQuery(uint Nonce, string GameId) : Message;
public sealed record LobbyRoom(string Address, ushort Port, string Name, byte Players, byte MaxPlayers, bool HasPassword);
public sealed record LobbyList(uint Nonce, LobbyRoom[] Rooms) : Message;

public abstract record ReliableMessage;
/// <summary>A world event in a given stage. Client to server it is a request; server to clients it is authoritative.</summary>
public sealed record WorldEvent(byte SourcePlayer, string Stage, int Scenario, GameEvent Event, bool Applied) : ReliableMessage;
public sealed record PlayerJoined(byte PlayerId, string Name) : ReliableMessage;
public sealed record PlayerLeft(byte PlayerId) : ReliableMessage;
public sealed record HostChanged(byte PlayerId) : ReliableMessage;
public sealed record Travel(string Stage, int Scenario) : ReliableMessage;
public sealed record StageEnter(string Stage, int Scenario) : ReliableMessage;
public sealed record Chat(string Name, string Text) : ReliableMessage;
public sealed record Rename(byte PlayerId, string Name) : ReliableMessage;

public static class Codec
{
    public const byte Magic0 = (byte)'G';
    public const byte Magic1 = (byte)'C';
    public const byte ProtocolVersion = 2;
    public const int DefaultPort = 7777;
    public const int DefaultLobbyPort = 7700;
    /// <summary>Rooms per LobbyList reply, which keeps the datagram well under a typical MTU.</summary>
    public const int MaxLobbyRooms = 20;

    static PacketWriter Header(MessageType t) =>
        new PacketWriter().U8(Magic0).U8(Magic1).U8(ProtocolVersion).U8((byte)t);

    public static byte[] Encode(Message m)
    {
        switch (m)
        {
            case Hello h:
                return Header(MessageType.Hello).U32(h.Nonce).Str(h.Name).Str(h.GameId).Str(h.Password).ToArray();
            case Welcome w:
                return Header(MessageType.Welcome).U32(w.Nonce).U8(w.PlayerId).U8(w.HostId).U32(w.SessionId).ToArray();
            case Reject r:
                return Header(MessageType.Reject).U32(r.Nonce).Str(r.Reason).ToArray();
            case Snapshot s:
            {
                var body = new byte[MailboxLayout.PsSize];
                s.State.WriteBody(body);
                return Header(MessageType.Snapshot).U8(s.PlayerId).U32(s.SenderTimeMs).U32(s.Sequence)
                    .Bytes(body.AsSpan(MailboxLayout.PsFrame)).ToArray();
            }
            case ReliablePacket p:
                return Header(MessageType.Reliable).U32(p.Sequence).Bytes(p.Payload).ToArray();
            case Ack a:
            {
                var w = Header(MessageType.Ack).U16((ushort)a.Sequences.Length);
                foreach (var s in a.Sequences) w.U32(s);
                return w.ToArray();
            }
            case Ping p:
                return Header(MessageType.Ping).U32(p.TimeMs).ToArray();
            case Pong p:
                return Header(MessageType.Pong).U32(p.TimeMs).ToArray();
            case Bye:
                return Header(MessageType.Bye).ToArray();
            case Boss b:
                return Header(MessageType.Boss).U8(b.PlayerId).U32(b.Key).Vec(b.Pos).Vec(b.Rot).Bool(b.Active).ToArray();
            case LobbyRegister l:
                return Header(MessageType.LobbyRegister).Str(l.RoomName).Str(l.GameId).U8(l.Players).U8(l.MaxPlayers).Bool(l.HasPassword).ToArray();
            case LobbyRegistered l:
                return Header(MessageType.LobbyRegistered).Str(l.PublicAddress).U16(l.PublicPort).ToArray();
            case LobbyUnregister:
                return Header(MessageType.LobbyUnregister).ToArray();
            case LobbyQuery q:
                return Header(MessageType.LobbyQuery).U32(q.Nonce).Str(q.GameId).ToArray();
            case LobbyList l:
            {
                var w = Header(MessageType.LobbyList).U32(l.Nonce).U8((byte)Math.Min(l.Rooms.Length, MaxLobbyRooms));
                foreach (var room in l.Rooms.Take(MaxLobbyRooms))
                    w.Str(room.Address).U16(room.Port).Str(room.Name).U8(room.Players).U8(room.MaxPlayers).Bool(room.HasPassword);
                return w.ToArray();
            }
            default:
                throw new ArgumentException($"cannot encode {m.GetType().Name}");
        }
    }

    /// <summary>Decodes a datagram; returns null for foreign or malformed packets.</summary>
    public static Message? Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new PacketReader(data);
            if (r.U8() != Magic0 || r.U8() != Magic1 || r.U8() != ProtocolVersion) return null;
            switch ((MessageType)r.U8())
            {
                case MessageType.Hello:
                    return new Hello(r.U32(), r.Str(), r.Str(), r.Str());
                case MessageType.Welcome:
                    return new Welcome(r.U32(), r.U8(), r.U8(), r.U32());
                case MessageType.Reject:
                    return new Reject(r.U32(), r.Str());
                case MessageType.Snapshot:
                {
                    byte id = r.U8();
                    uint time = r.U32(), seq = r.U32();
                    var body = new byte[MailboxLayout.PsSize];
                    r.Bytes(MailboxLayout.PsSize - MailboxLayout.PsFrame).CopyTo(body.AsSpan(MailboxLayout.PsFrame));
                    return new Snapshot(id, time, seq, PlayerState.Read(body));
                }
                case MessageType.Reliable:
                    return new ReliablePacket(r.U32(), r.Rest().ToArray());
                case MessageType.Ack:
                {
                    var seqs = new uint[r.U16()];
                    for (int i = 0; i < seqs.Length; i++) seqs[i] = r.U32();
                    return new Ack(seqs);
                }
                case MessageType.Ping:
                    return new Ping(r.U32());
                case MessageType.Pong:
                    return new Pong(r.U32());
                case MessageType.Bye:
                    return new Bye();
                case MessageType.Boss:
                    return new Boss(r.U8(), r.U32(), r.Vec(), r.Vec(), r.Bool());
                case MessageType.LobbyRegister:
                    return new LobbyRegister(r.Str(), r.Str(), r.U8(), r.U8(), r.Bool());
                case MessageType.LobbyRegistered:
                    return new LobbyRegistered(r.Str(), r.U16());
                case MessageType.LobbyUnregister:
                    return new LobbyUnregister();
                case MessageType.LobbyQuery:
                    return new LobbyQuery(r.U32(), r.Str());
                case MessageType.LobbyList:
                {
                    uint nonce = r.U32();
                    var rooms = new LobbyRoom[r.U8()];
                    for (int i = 0; i < rooms.Length; i++)
                        rooms[i] = new LobbyRoom(r.Str(), r.U16(), r.Str(), r.U8(), r.U8(), r.Bool());
                    return new LobbyList(nonce, rooms);
                }
                default:
                    return null;
            }
        }
        catch (MalformedPacketException)
        {
            return null;
        }
    }

    public static byte[] EncodeReliable(ReliableMessage m)
    {
        var w = new PacketWriter();
        switch (m)
        {
            case WorldEvent e:
            {
                var ev = new byte[MailboxLayout.EvSize];
                e.Event.Write(ev);
                w.U8((byte)(e.Applied ? ReliableType.EventApplied : ReliableType.EventRequest))
                    .U8(e.SourcePlayer).Str(e.Stage).S32(e.Scenario).Bytes(ev);
                break;
            }
            case PlayerJoined j: w.U8((byte)ReliableType.PlayerJoined).U8(j.PlayerId).Str(j.Name); break;
            case PlayerLeft l: w.U8((byte)ReliableType.PlayerLeft).U8(l.PlayerId); break;
            case HostChanged h: w.U8((byte)ReliableType.HostChanged).U8(h.PlayerId); break;
            case Travel t: w.U8((byte)ReliableType.Travel).Str(t.Stage).S32(t.Scenario); break;
            case StageEnter s: w.U8((byte)ReliableType.StageEnter).Str(s.Stage).S32(s.Scenario); break;
            case Chat c: w.U8((byte)ReliableType.Chat).Str(c.Name).Str(c.Text); break;
            case Rename n: w.U8((byte)ReliableType.Rename).U8(n.PlayerId).Str(n.Name); break;
            default: throw new ArgumentException($"cannot encode {m.GetType().Name}");
        }
        return w.ToArray();
    }

    public static ReliableMessage? DecodeReliable(ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new PacketReader(data);
            var type = (ReliableType)r.U8();
            switch (type)
            {
                case ReliableType.EventRequest:
                case ReliableType.EventApplied:
                {
                    byte src = r.U8();
                    string stage = r.Str();
                    int scenario = r.S32();
                    var ev = GameEvent.Read(r.Bytes(MailboxLayout.EvSize));
                    return new WorldEvent(src, stage, scenario, ev, type == ReliableType.EventApplied);
                }
                case ReliableType.PlayerJoined: return new PlayerJoined(r.U8(), r.Str());
                case ReliableType.PlayerLeft: return new PlayerLeft(r.U8());
                case ReliableType.HostChanged: return new HostChanged(r.U8());
                case ReliableType.Travel: return new Travel(r.Str(), r.S32());
                case ReliableType.StageEnter: return new StageEnter(r.Str(), r.S32());
                case ReliableType.Chat: return new Chat(r.Str(), r.Str());
                case ReliableType.Rename: return new Rename(r.U8(), r.Str());
                default: return null;
            }
        }
        catch (MalformedPacketException)
        {
            return null;
        }
    }
}
