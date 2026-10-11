using System.Numerics;
using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;
using GalaxyCoop.Server;

namespace GalaxyCoop.Tests;

public class ServerTests
{
    sealed class World
    {
        public readonly FakeNetwork Net;
        public readonly RelayServer Server;
        public readonly List<NetClient> Clients = new();
        public readonly List<string> Log = new();
        public long Now;
        readonly FakeNetwork.Endpoint _serverEp;

        public World(ServerOptions? options = null, int seed = 1)
        {
            Net = new FakeNetwork(seed);
            RelayServer? server = null;
            _serverEp = Net.Join(7777, (from, data) => server!.OnPacket(from, data, Now));
            Server = server = new RelayServer(_serverEp, options);
            Server.Log += Log.Add;
        }

        public NetClient AddClient(string name, string password = "", string game = "SB4E")
        {
            NetClient? c = null;
            var ep = Net.Join(9000 + Clients.Count, (from, data) => c!.OnPacket(from, data, Now));
            c = new NetClient(ep, _serverEp.Address, name, password, game);
            Clients.Add(c);
            return c;
        }

        public void Run(int ms, int step = 10)
        {
            for (int t = 0; t < ms; t += step)
            {
                Now += step;
                foreach (var c in Clients) c.Tick(Now);
                Server.Tick(Now);
                Net.Pump();
            }
        }
    }

    static GameEvent Coin(uint key) => new() { Type = GameEventType.Coin, Key = key };

    [Fact]
    public void FirstPlayerIsHostAndEveryoneSeesTheRoster()
    {
        var w = new World();
        var a = w.AddClient("Mario");
        w.Run(100);
        var b = w.AddClient("Luigi");
        w.Run(200);

        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal("Luigi", Assert.Single(a.Peers).Name);
        Assert.Equal("Mario", Assert.Single(b.Peers).Name);
        Assert.NotEqual(a.PlayerId, b.PlayerId);
    }

    [Fact]
    public void RejectsWrongPasswordGameAndFullServer()
    {
        var w = new World(new ServerOptions { Password = "star", MaxPlayers = 1 });
        var ok = w.AddClient("ok", "star");
        var badPw = w.AddClient("pw", "nope");
        var badGame = w.AddClient("game", "star", "RMGE");
        w.Run(200);
        var full = w.AddClient("full", "star");
        w.Run(200);

        Assert.True(ok.IsConnected);
        Assert.Equal(ClientState.Rejected, badPw.State);
        Assert.Equal(ClientState.Rejected, badGame.State);
        Assert.Equal(ClientState.Rejected, full.State);
    }

    [Fact]
    public void SnapshotsAreRelayedAndStampedWithSenderId()
    {
        var w = new World();
        var a = w.AddClient("a");
        var b = w.AddClient("b");
        w.Run(200);
        for (int i = 0; i < 10; i++)
        {
            a.SendSnapshot(new PlayerState { Pos = new Vector3(i, 0, 0), Stage = "S", Flags = PlayerFlags.InScene, Active = true }, w.Now);
            w.Run(33);
        }
        var peer = Assert.Single(b.Peers);
        Assert.Equal(a.PlayerId, peer.Id);
        Assert.Equal(0, peer.Slot);
        Assert.True(peer.Interpolator.Count >= 10);
        Assert.Equal("S", w.Server.Peers.Single(p => p.Id == a.PlayerId).Stage);
    }

    [Fact]
    public void SameCoinCollectedTwiceIsAppliedOnce()
    {
        var w = new World();
        var a = w.AddClient("a");
        var b = w.AddClient("b");
        w.Run(200);
        a.SendStageEnter("S", 1, w.Now);
        b.SendStageEnter("S", 1, w.Now);
        w.Run(100);

        a.SendEvent("S", 1, Coin(42), w.Now);
        b.SendEvent("S", 1, Coin(42), w.Now);
        a.SendEvent("S", 1, Coin(43), w.Now);
        w.Run(200);

        var toB = b.IncomingEvents.Select(e => e.Event.Key).ToList();
        var toA = a.IncomingEvents.Select(e => e.Event.Key).ToList();
        Assert.Equal(new uint[] { 42, 43 }, toB);
        Assert.Empty(toA);
        Assert.All(b.IncomingEvents, e => Assert.Equal(a.PlayerId, e.SourcePlayer));
    }

    [Fact]
    public void LateJoinerGetsStageReplayButEmptyStagesReset()
    {
        var w = new World();
        var a = w.AddClient("a");
        w.Run(200);
        a.SendStageEnter("S", 1, w.Now);
        a.SendEvent("S", 1, Coin(1), w.Now);
        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.SwitchOn, Key = 7 }, w.Now);
        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.SwitchOn, Key = 8 }, w.Now);
        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.SwitchOff, Key = 8 }, w.Now);
        w.Run(200);

        var b = w.AddClient("b");
        w.Run(200);
        b.SendStageEnter("S", 1, w.Now);
        w.Run(200);
        var replay = b.IncomingEvents.Select(e => (e.Event.Type, e.Event.Key)).ToList();
        Assert.Equal(new[] { (GameEventType.Coin, 1u), (GameEventType.SwitchOn, 7u) }, replay);

        // Everyone leaves S, then a re-enters: a fresh visit, like the base game.
        a.SendStageEnter("T", 1, w.Now);
        b.SendStageEnter("T", 1, w.Now);
        w.Run(100);
        Assert.Null(w.Server.GetStage("S", 1));
    }

    [Fact]
    public void CometStaysOnForALateJoinerUntilItEnds()
    {
        var w = new World();
        var a = w.AddClient("a");
        w.Run(200);
        a.SendStageEnter("S", 1, w.Now);
        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.CometOn }, w.Now);
        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.CometOn }, w.Now);
        w.Run(200);
        Assert.Empty(a.IncomingEvents);

        var b = w.AddClient("b");
        w.Run(200);
        b.SendStageEnter("S", 1, w.Now);
        w.Run(200);
        Assert.Equal(GameEventType.CometOn, Assert.Single(b.IncomingEvents).Event.Type);

        a.SendEvent("S", 1, new GameEvent { Type = GameEventType.CometOff }, w.Now);
        w.Run(200);
        Assert.Contains(b.IncomingEvents, e => e.Event.Type == GameEventType.CometOff);
        Assert.Empty(a.IncomingEvents);
    }

    [Fact]
    public void SoloSceneRestartResetsTheStage()
    {
        var w = new World();
        var a = w.AddClient("a");
        w.Run(200);
        a.SendStageEnter("S", 1, w.Now);
        a.SendEvent("S", 1, Coin(1), w.Now);
        w.Run(100);
        Assert.Equal(1, w.Server.GetStage("S", 1)!.TakenCount);
        a.SendStageEnter("S", 1, w.Now);
        w.Run(100);
        Assert.Null(w.Server.GetStage("S", 1));
    }

    [Fact]
    public void HostTravelIsForwardedAndHostMigratesOnLeave()
    {
        var w = new World();
        var a = w.AddClient("a");
        w.Run(100);
        var b = w.AddClient("b");
        var c = w.AddClient("c");
        w.Run(200);

        a.SendStageEnter("IslandFleetGalaxy", 2, w.Now);
        w.Run(100);
        Assert.Equal(new Travel("IslandFleetGalaxy", 2), b.PendingTravel);
        Assert.Equal(new Travel("IslandFleetGalaxy", 2), c.PendingTravel);

        a.Disconnect();
        w.Run(100);
        Assert.True(b.IsHost);
        Assert.Equal(b.PlayerId, c.HostId);
        Assert.DoesNotContain(c.Peers, p => p.Id == a.PlayerId);
    }

    [Fact]
    public void SilentClientTimesOutAndOthersAreTold()
    {
        var w = new World(new ServerOptions { TimeoutMs = 1000 });
        var a = w.AddClient("a");
        var b = w.AddClient("b");
        w.Run(200);
        var removed = new List<byte>();
        b.PeerRemoved += p => removed.Add(p.Id);

        w.Clients.Remove(a);
        w.Net.Leave(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9000));
        w.Run(1500);
        Assert.Equal(new[] { a.PlayerId }, removed);
        Assert.True(b.IsHost);
    }

    [Fact]
    public void ReliableEventsSurviveALossyNetwork()
    {
        var w = new World(seed: 1234);
        var a = w.AddClient("a");
        var b = w.AddClient("b");
        w.Run(300);
        w.Net.DropRate = 0.3;
        w.Net.DuplicateRate = 0.2;
        w.Net.Reorder = true;
        a.SendStageEnter("S", 1, w.Now);
        b.SendStageEnter("S", 1, w.Now);
        for (uint k = 1; k <= 50; k++) a.SendEvent("S", 1, Coin(k), w.Now);
        w.Run(5000);

        Assert.True(a.IsConnected && b.IsConnected);
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (uint)i), b.IncomingEvents.Select(e => e.Event.Key));
    }

    [Fact]
    public void ClientReconnectsAfterServerSilence()
    {
        var w = new World();
        var a = w.AddClient("a");
        w.Run(200);
        Assert.True(a.IsConnected);
        w.Net.DropRate = 1;
        w.Run(11_000);
        Assert.Equal(ClientState.Connecting, a.State);
        w.Net.DropRate = 0;
        w.Run(1000);
        Assert.True(a.IsConnected);
    }
}
