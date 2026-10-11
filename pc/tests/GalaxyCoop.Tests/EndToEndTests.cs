using System.Numerics;
using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;
using GalaxyCoop.Server;

namespace GalaxyCoop.Tests;

/// <summary>Two fake games, two companions, one server; everything but Dolphin and the sockets.</summary>
public class EndToEndTests
{
    sealed class Player
    {
        public readonly FakeGameMemory Game = new();
        public readonly GameLink Link;
        public readonly NetClient Client;
        public readonly ConnectMode.Session Session;

        public Player(FakeNetwork net, System.Net.IPEndPoint server, int port, string name, Func<long> now)
        {
            Game.InstallMailbox(0x10000);
            NetClient? c = null;
            var ep = net.Join(port, (from, data) => c!.OnPacket(from, data, now()));
            Client = c = new NetClient(ep, server, name);
            Link = new GameLink(() => Game);
            Session = new ConnectMode.Session(Client, Link, followHost: true, CompanionFlags.None);
        }

        public void Step(long now)
        {
            Client.Tick(now);
            Session.Step(now);
        }
    }

    static PlayerState InStage(string stage, float x) => new()
    {
        Pos = new Vector3(x, 0, 0), Up = Vector3.UnitY, Front = Vector3.UnitZ, Stage = stage, Scenario = 1,
        Flags = PlayerFlags.InScene, Active = true, BckName = "Run",
    };

    [Theory]
    [InlineData("IslandFleetGalaxy", true)]
    [InlineData("MarioFaceShipGalaxy", true)]
    [InlineData("FileSelect", false)]
    [InlineData("WorldMap01Galaxy", false)]
    [InlineData("PeachCastleGalaxy", false)]
    [InlineData("", false)]
    public void OnlyFollowsTheHostBetweenPlayableGalaxies(string stage, bool playable) =>
        Assert.Equal(playable, ConnectMode.IsPlayableStage(stage));

    [Fact]
    public void JoinerInTheOpeningStoryIsSentToTheShip()
    {
        Assert.True(ConnectMode.IsIntroStage("PeachCastleGalaxy"));
        Assert.True(ConnectMode.IsIntroStage("Storybook"));
        Assert.False(ConnectMode.IsIntroStage("MarioFaceShipGalaxy"));

        long now = 0;
        var net = new FakeNetwork();
        RelayServer? server = null;
        var serverEp = net.Join(7777, (from, data) => server!.OnPacket(from, data, now));
        server = new RelayServer(serverEp);
        var a = new Player(net, serverEp.Address, 9101, "Mario", () => now);
        var b = new Player(net, serverEp.Address, 9102, "Luigi", () => now);

        void Run(int ms)
        {
            for (int t = 0; t < ms; t += 8)
            {
                now += 8;
                a.Step(now);
                b.Step(now);
                server.Tick(now);
                net.Pump();
            }
        }

        Run(100);
        b.Game.GameWriteLocal(InStage("PeachCastleGalaxy", 0));
        Run(100);
        Assert.Equal(0u, b.Game.GameReadTravel().serial);

        for (int i = 0; i < 10; i++)
        {
            a.Game.GameWriteLocal(InStage("MarioFaceShipGalaxy", 10));
            Run(16);
        }

        var (serial, scenario, stage) = b.Game.GameReadTravel();
        Assert.True(serial >= 1);
        Assert.Equal(("MarioFaceShipGalaxy", 1), (stage, scenario));
    }

    [Fact]
    public void PlayersSeeEachOtherShareEventsAndFollowTheHost()
    {
        long now = 0;
        var net = new FakeNetwork();
        RelayServer? server = null;
        var serverEp = net.Join(7777, (from, data) => server!.OnPacket(from, data, now));
        server = new RelayServer(serverEp);
        var a = new Player(net, serverEp.Address, 9001, "Mario", () => now);
        var b = new Player(net, serverEp.Address, 9002, "Luigi", () => now);

        void Run(int ms)
        {
            for (int t = 0; t < ms; t += 8)
            {
                now += 8;
                a.Step(now);
                b.Step(now);
                server.Tick(now);
                net.Pump();
            }
        }

        Run(100);
        b.Game.GameWriteLocal(InStage("HomeGalaxy", 0));
        Run(200);
        Assert.True(a.Client.IsHost);

        // Host enters a galaxy: B is asked to travel there.
        for (int i = 0; i < 30; i++)
        {
            a.Game.GameWriteLocal(InStage("IslandFleetGalaxy", 100 + i));
            Run(16);
        }
        var (serial, scenario, stage) = b.Game.GameReadTravel();
        Assert.Equal(1u, serial);
        Assert.Equal(("IslandFleetGalaxy", 1), (stage, scenario));

        // B arrives; A's ghost shows up in B's slot 0, interpolated from A's movement.
        b.Game.GameWriteLocal(InStage("IslandFleetGalaxy", 0));
        Run(300);
        var (_, ghost) = b.Game.GameReadRemote(0);
        Assert.True(ghost.Active);
        Assert.Equal(a.Client.PlayerId, ghost.PlayerId);
        Assert.InRange(ghost.Pos.X, 100, 130);
        Assert.Equal("Run", ghost.BckName);

        // A collects a coin: B's game receives exactly one event for it.
        a.Game.GamePushEvent(new GameEvent { Type = GameEventType.Coin, Key = 0x1234 });
        b.Game.GamePushEvent(new GameEvent { Type = GameEventType.Coin, Key = 0x1234 });
        Run(200);
        var toB = b.Game.GamePopEvents();
        var toA = a.Game.GamePopEvents();
        Assert.Equal(0x1234u, Assert.Single(toB).Key);
        Assert.Equal(a.Client.PlayerId, toB[0].Source);
        Assert.Empty(toA);

        // A loses a life and hits a switch: deaths are only logged, the switch reaches B's game.
        a.Game.GamePushEvent(new GameEvent { Type = GameEventType.PlayerDeath });
        a.Game.GamePushEvent(new GameEvent { Type = GameEventType.SwitchOn, Key = 0x00030007 });
        Run(200);
        var world = b.Game.GamePopEvents();
        Assert.Equal(GameEventType.SwitchOn, Assert.Single(world).Type);
        Assert.Equal(0x00030007u, world[0].Key);

        // A quits: B's ghost slot is cleared.
        a.Client.Disconnect();
        net.Leave(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9001));
        for (int t = 0; t < 300; t += 8)
        {
            now += 8;
            b.Step(now);
            server.Tick(now);
            net.Pump();
        }
        Assert.False(b.Game.GameReadRemote(0).state.Active);
        Assert.True(b.Client.IsHost);
    }
}
