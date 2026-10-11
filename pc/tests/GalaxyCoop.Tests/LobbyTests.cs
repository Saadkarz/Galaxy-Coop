using System.Net;
using GalaxyCoop.Protocol.Net;
using GalaxyCoop.Server;

namespace GalaxyCoop.Tests;

public class LobbyTests
{
    sealed class Rig
    {
        public long Now;
        public readonly FakeNetwork Net = new();
        public readonly LobbyServer Lobby;
        public readonly IPEndPoint LobbyEp;
        public readonly List<Message> Inbox = new();
        public readonly IPEndPoint BrowserEp;
        readonly FakeNetwork.Endpoint _browser;

        public Rig()
        {
            LobbyServer? lobby = null;
            var ep = Net.Join(7700, (from, data) => lobby!.OnPacket(from, data, Now));
            Lobby = lobby = new LobbyServer(ep) { ExpireMs = 1000 };
            LobbyEp = ep.Address;
            _browser = Net.Join(9100, (from, data) => { if (Codec.Decode(data) is { } m) Inbox.Add(m); });
            BrowserEp = _browser.Address;
        }

        public RelayServer Host(int port, ServerOptions options)
        {
            RelayServer? server = null;
            var ep = Net.Join(port, (from, data) => server!.OnPacket(from, data, Now));
            return server = new RelayServer(ep, options with { Lobby = LobbyEp, LobbyIntervalMs = 200 });
        }

        public LobbyRoom[] Query(string game = "SB4E")
        {
            Inbox.Clear();
            _browser.Send(LobbyEp, Codec.Encode(new LobbyQuery(42, game)));
            Net.Pump();
            return Assert.IsType<LobbyList>(Assert.Single(Inbox)).Rooms;
        }
    }

    [Fact]
    public void PublicServersAreListedWithTheAddressTheLobbySees()
    {
        var rig = new Rig();
        var host = rig.Host(7777, new ServerOptions { RoomName = "Fahd's galaxy", Password = "pw" });
        host.Tick(rig.Now);
        rig.Net.Pump();

        var room = Assert.Single(rig.Query());
        Assert.Equal(("Fahd's galaxy", (ushort)7777, true), (room.Name, room.Port, room.HasPassword));
        Assert.Equal(new IPEndPoint(IPAddress.Parse(room.Address), room.Port), host.PublicEndPoint);
        Assert.Empty(rig.Query("RMGE"));
    }

    [Fact]
    public void RoomsDisappearOnShutdownOrWhenTheyStopRegistering()
    {
        var rig = new Rig();
        var a = rig.Host(7777, new ServerOptions { RoomName = "A" });
        var b = rig.Host(7778, new ServerOptions { RoomName = "B" });
        a.Tick(rig.Now);
        b.Tick(rig.Now);
        rig.Net.Pump();
        Assert.Equal(2, rig.Query().Length);

        a.Shutdown();
        rig.Net.Pump();
        Assert.Equal("B", Assert.Single(rig.Query()).Name);

        rig.Now += 1500;
        rig.Lobby.Tick(rig.Now);
        Assert.Empty(rig.Query());
    }

    [Fact]
    public void FullRoomsAreNotOffered()
    {
        var rig = new Rig();
        var host = rig.Host(7777, new ServerOptions { RoomName = "Tiny", MaxPlayers = 1 });
        host.OnPacket(rig.BrowserEp, Codec.Encode(new Hello(1, "Mario", "SB4E", "")), rig.Now);
        host.Tick(rig.Now);
        rig.Net.Pump();
        Assert.Empty(rig.Query());
    }

    [Fact]
    public void LobbyMessagesRoundTrip()
    {
        var list = new LobbyList(7, new[] { new LobbyRoom("203.0.113.5", 7777, "Room", 2, 8, false) });
        var decoded = Assert.IsType<LobbyList>(Codec.Decode(Codec.Encode(list)));
        Assert.Equal(list.Nonce, decoded.Nonce);
        Assert.Equal(list.Rooms, decoded.Rooms);
        Assert.Equal(new LobbyRegister("R", "SB4E", 1, 4, true), Codec.Decode(Codec.Encode(new LobbyRegister("R", "SB4E", 1, 4, true))));
    }
}
