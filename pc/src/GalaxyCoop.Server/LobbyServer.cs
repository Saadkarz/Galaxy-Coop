using System.Net;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Server;

/// <summary>
/// The public room list. Public servers re-register every few seconds from
/// their game socket; a room disappears when its registrations stop. Like
/// RelayServer it is socket-free: feed it datagrams and ticks.
/// </summary>
public sealed class LobbyServer(ITransport transport)
{
    public sealed record Room(IPEndPoint EndPoint, LobbyRegister Info, long LastSeen);

    readonly Dictionary<IPEndPoint, Room> _rooms = new();

    public int ExpireMs { get; init; } = 20000;
    public int MaxRooms { get; init; } = 500;
    public IReadOnlyCollection<Room> Rooms => _rooms.Values;
    public event Action<string>? Log;

    public void OnPacket(IPEndPoint from, ReadOnlySpan<byte> data, long nowMs)
    {
        switch (Codec.Decode(data))
        {
            case LobbyRegister reg:
                if (!_rooms.ContainsKey(from))
                {
                    if (_rooms.Count >= MaxRooms) return;
                    Log?.Invoke($"listed '{reg.RoomName}' at {from}");
                }
                _rooms[from] = new Room(from, reg, nowMs);
                transport.Send(from, Codec.Encode(new LobbyRegistered(from.Address.ToString(), (ushort)from.Port)));
                break;
            case LobbyUnregister:
                if (_rooms.Remove(from, out var gone)) Log?.Invoke($"unlisted '{gone.Info.RoomName}' at {from}");
                break;
            case LobbyQuery q:
                var rooms = _rooms.Values
                    .Where(r => r.Info.GameId == q.GameId && nowMs - r.LastSeen <= ExpireMs && r.Info.Players < r.Info.MaxPlayers)
                    .OrderByDescending(r => r.Info.Players)
                    .ThenBy(r => r.Info.RoomName, StringComparer.OrdinalIgnoreCase)
                    .Take(Codec.MaxLobbyRooms)
                    .Select(r => new LobbyRoom(r.EndPoint.Address.ToString(), (ushort)r.EndPoint.Port, r.Info.RoomName,
                        r.Info.Players, r.Info.MaxPlayers, r.Info.HasPassword))
                    .ToArray();
                transport.Send(from, Codec.Encode(new LobbyList(q.Nonce, rooms)));
                break;
        }
    }

    public void Tick(long nowMs)
    {
        foreach (var room in _rooms.Values.Where(r => nowMs - r.LastSeen > ExpireMs).ToList())
        {
            _rooms.Remove(room.EndPoint);
            Log?.Invoke($"'{room.Info.RoomName}' at {room.EndPoint} went quiet; unlisted");
        }
    }
}
