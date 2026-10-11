using System.Numerics;
using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;

namespace GalaxyCoop.Tests;

public class MailboxBridgeTests
{
    static (FakeGameMemory mem, MailboxBridge bridge) Setup(int offset = 0x2345A0)
    {
        var mem = new FakeGameMemory();
        // A decoy: magic without valid header fields must be ignored.
        BE.U32(mem.Ram, 0x1000, MailboxLayout.Magic0);
        BE.U32(mem.Ram, 0x1004, MailboxLayout.Magic1);
        mem.InstallMailbox(offset);
        var bridge = new MailboxBridge(mem);
        Assert.True(bridge.TryLocate());
        return (mem, bridge);
    }

    [Fact]
    public void LocatesMailboxAcrossChunkBoundary()
    {
        var (_, bridge) = Setup(0x100000 - 4);
        Assert.Equal(FakeGameMemory.HostBase + 0x100000 - 4, bridge.Address);
        Assert.Equal("MEM1 0x800FFFFC", bridge.AddressDescription);
    }

    [Fact]
    public void ReadsLocalStateAndSkipsTornWrites()
    {
        var (mem, bridge) = Setup();
        var state = new PlayerState { Frame = 9, Pos = new Vector3(10, 20, 30), Stage = "RedBlueExGalaxy", BckName = "Wait", Active = true, Flags = PlayerFlags.InScene };
        mem.GameWriteLocal(state);
        var snap = bridge.Poll()!;
        Assert.Equal(state with { Seq = 2 }, snap.Local);

        mem.GameBeginTornWrite();
        Assert.Null(bridge.Poll()!.Local);
    }

    [Fact]
    public void DrainsOutgoingEventsOnce()
    {
        var (mem, bridge) = Setup();
        mem.GamePushEvent(new GameEvent { Type = GameEventType.Coin, Key = 1 });
        mem.GamePushEvent(new GameEvent { Type = GameEventType.StarPiece, Key = 2 });
        var first = bridge.Poll()!.OutgoingEvents;
        Assert.Equal(new uint[] { 1, 2 }, first.Select(e => e.Key));
        Assert.Equal(2u, mem.OutTail);
        Assert.Empty(bridge.Poll()!.OutgoingEvents);
    }

    [Fact]
    public void IncomingRingRespectsCapacity()
    {
        var (mem, bridge) = Setup();
        for (int i = 0; i < MailboxLayout.EventRingSize; i++)
            Assert.True(bridge.PushIncomingEvent(new GameEvent { Type = GameEventType.Coin, Key = (uint)i }));
        Assert.False(bridge.PushIncomingEvent(new GameEvent { Type = GameEventType.Coin }));
        var popped = mem.GamePopEvents();
        Assert.Equal(MailboxLayout.EventRingSize, popped.Count);
        Assert.Equal(1u, popped[0].Id);
        Assert.True(bridge.PushIncomingEvent(new GameEvent { Type = GameEventType.SwitchOn, Payload = "X" }));
    }

    [Fact]
    public void TravelRequestBumpsSerialAfterWritingStage()
    {
        var (mem, bridge) = Setup();
        Assert.True(bridge.RequestTravel("SubSpaceKoopaJrCastleGalaxy", 3));
        Assert.True(bridge.RequestTravel("IslandFleetGalaxy", 1));
        var (serial, scenario, stage) = mem.GameReadTravel();
        Assert.Equal(2u, serial);
        Assert.Equal(1, scenario);
        Assert.Equal("IslandFleetGalaxy", stage);
    }

    [Fact]
    public void RemoteWritesLeaveEvenSequence()
    {
        var (mem, bridge) = Setup();
        var s = new PlayerState { Active = true, Pos = new Vector3(1, 2, 3), PlayerId = 3 };
        Assert.True(bridge.WriteRemote(2, s));
        Assert.True(bridge.WriteRemote(2, s));
        var (seq, read) = mem.GameReadRemote(2);
        Assert.Equal(4u, seq);
        Assert.Equal(s with { Seq = 4 }, read);
    }

    [Fact]
    public void GameLinkReattachesAfterReset()
    {
        var mem = new FakeGameMemory();
        mem.InstallMailbox(0x8000);
        int attaches = 0;
        using var link = new GameLink(() => mem);
        link.Attached += () => attaches++;
        Assert.NotNull(link.Poll());
        link.Bridge!.WriteHeartbeat(CompanionFlags.Attached);
        Assert.Equal(1u, mem.Heartbeat);

        mem.Corrupt();
        Assert.Null(link.Poll());
        mem.InstallMailbox(0x9000);
        Assert.False(link.IsReady);
        Assert.Equal(1, attaches);
    }
}
