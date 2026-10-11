using System.Numerics;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Tests;

public class ProtocolTests
{
    static readonly PlayerState Sample = new()
    {
        Frame = 77, Pos = new Vector3(100, 200, -300), Up = Vector3.UnitY, Front = Vector3.UnitZ,
        Flags = PlayerFlags.InScene, Stage = "IslandFleetGalaxy", Scenario = 1, BckName = "Run", Active = true,
    };

    public static IEnumerable<object[]> Messages() => new[]
    {
        new object[] { new Hello(42, "Fahd", "SB4E", "pw") },
        new object[] { new Welcome(42, 3, 1, 0xCAFEBABE) },
        new object[] { new Reject(42, "server full") },
        new object[] { new Ping(123456) },
        new object[] { new Pong(123456) },
        new object[] { new Boss(2, 0xB055, new Vector3(1, 2, 3), new Vector3(0, 90, 0), true) },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void SimpleMessagesRoundTrip(Message m) => Assert.Equal(m, Codec.Decode(Codec.Encode(m)));

    [Fact]
    public void SnapshotRoundTrips()
    {
        var s = new Snapshot(5, 0xFFFFFFF0, 9, Sample);
        var back = Assert.IsType<Snapshot>(Codec.Decode(Codec.Encode(s)));
        Assert.Equal(s with { State = Sample with { Seq = 0 } }, back);
    }

    [Fact]
    public void AckAndReliableRoundTrip()
    {
        var a = Assert.IsType<Ack>(Codec.Decode(Codec.Encode(new Ack(new uint[] { 1, 5, 9 }))));
        Assert.Equal(new uint[] { 1, 5, 9 }, a.Sequences);
        var r = Assert.IsType<ReliablePacket>(Codec.Decode(Codec.Encode(new ReliablePacket(7, new byte[] { 1, 2, 3 }))));
        Assert.Equal(7u, r.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3 }, r.Payload);
    }

    public static IEnumerable<object[]> ReliableMessages() => new[]
    {
        new object[] { new WorldEvent(2, "RedBlueExGalaxy", 1, new GameEvent { Type = GameEventType.Coin, Key = 0xABCD, Pos = new Vector3(1, 2, 3) }, false) },
        new object[] { new WorldEvent(2, "RedBlueExGalaxy", 1, new GameEvent { Type = GameEventType.SwitchOn, Key = 9 }, true) },
        new object[] { new PlayerJoined(4, "Luigi") },
        new object[] { new PlayerLeft(4) },
        new object[] { new HostChanged(2) },
        new object[] { new Travel("SubSpaceKoopaJrCastleGalaxy", 3) },
        new object[] { new StageEnter("IslandFleetGalaxy", 1) },
        new object[] { new Chat("Fahd", "the comet is over here") },
        new object[] { new Rename(3, "Fahd") },
    };

    [Theory]
    [MemberData(nameof(ReliableMessages))]
    public void ReliableMessagesRoundTrip(ReliableMessage m) => Assert.Equal(m, Codec.DecodeReliable(Codec.EncodeReliable(m)));

    [Fact]
    public void ForeignAndTruncatedPacketsAreIgnored()
    {
        Assert.Null(Codec.Decode(new byte[] { 1, 2, 3, 4, 5 }));
        var hello = Codec.Encode(new Hello(1, "a", "SB4E", ""));
        Assert.Null(Codec.Decode(hello.AsSpan(0, hello.Length - 2)));
        var snap = Codec.Encode(new Snapshot(1, 1, 1, Sample));
        Assert.Null(Codec.Decode(snap.AsSpan(0, 20)));
        Assert.Null(Codec.DecodeReliable(new byte[] { 99 }));
    }
}
