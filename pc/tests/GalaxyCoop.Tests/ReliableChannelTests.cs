using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Tests;

public class ReliableChannelTests
{
    static ReliablePacket Decode(byte[] packet) => Assert.IsType<ReliablePacket>(Codec.Decode(packet));

    [Fact]
    public void DeliversInOrderExactlyOnceDespiteReorderingAndDuplicates()
    {
        var sender = new ReliableChannel();
        var receiver = new ReliableChannel();
        var packets = Enumerable.Range(1, 5).Select(i => Decode(sender.Send(new PlayerLeft((byte)i), 0))).ToList();

        var delivered = new List<byte>();
        foreach (var p in new[] { packets[2], packets[0], packets[0], packets[4], packets[1], packets[3], packets[2] })
            delivered.AddRange(receiver.Receive(p).Cast<PlayerLeft>().Select(m => m.PlayerId));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, delivered);
    }

    [Fact]
    public void ResendsUntilAckedThenStops()
    {
        var ch = new ReliableChannel { ResendIntervalMs = 100 };
        var first = Decode(ch.Send(new HostChanged(1), 0));
        Assert.Empty(ch.CollectResends(50));
        Assert.Single(ch.CollectResends(100));
        Assert.Empty(ch.CollectResends(150));
        Assert.Single(ch.CollectResends(200));
        ch.OnAck(new Ack(new[] { first.Sequence }));
        Assert.Empty(ch.CollectResends(1000));
        Assert.Equal(0, ch.PendingCount);
    }

    [Fact]
    public void FailsAfterMaxAttempts()
    {
        var ch = new ReliableChannel { ResendIntervalMs = 10, MaxAttempts = 3 };
        ch.Send(new HostChanged(1), 0);
        ch.CollectResends(10);
        ch.CollectResends(20);
        Assert.False(ch.Failed); // three transmissions so far
        ch.CollectResends(30);
        Assert.True(ch.Failed);
    }

    [Fact]
    public void IgnoresSequencesFarOutsideWindow()
    {
        var ch = new ReliableChannel();
        Assert.Empty(ch.Receive(new ReliablePacket(100_000, Codec.EncodeReliable(new HostChanged(1)))));
        Assert.Single(ch.Receive(new ReliablePacket(1, Codec.EncodeReliable(new HostChanged(1)))));
    }
}
