using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;
using L = GalaxyCoop.Protocol.MailboxLayout;

namespace GalaxyCoop.Tests;

/// <summary>A byte array pretending to be MEM1, plus helpers that play the game module's role.</summary>
public sealed class FakeGameMemory : IGameMemory
{
    public const long HostBase = 0x7FF0_0000_0000;
    public readonly byte[] Ram;
    public int MailboxOffset { get; private set; } = -1;
    public bool IsAlive { get; set; } = true;
    public IReadOnlyList<MemoryRegion> Regions { get; }

    public FakeGameMemory(int size = 0x400000)
    {
        Ram = new byte[size];
        Regions = new[] { new MemoryRegion(HostBase, 0x80000000, size, "MEM1") };
    }

    public bool Read(long hostAddress, Span<byte> destination)
    {
        long off = hostAddress - HostBase;
        if (off < 0 || off + destination.Length > Ram.Length) return false;
        Ram.AsSpan((int)off, destination.Length).CopyTo(destination);
        return true;
    }

    public bool Write(long hostAddress, ReadOnlySpan<byte> source)
    {
        long off = hostAddress - HostBase;
        if (off < 0 || off + source.Length > Ram.Length) return false;
        source.CopyTo(Ram.AsSpan((int)off));
        return true;
    }

    public void Dispose() { }

    Span<byte> Mb => Ram.AsSpan(MailboxOffset, L.MbSize);

    public void InstallMailbox(int offset)
    {
        MailboxOffset = offset;
        Mb.Clear();
        BE.U32(Mb, L.MbVersion, L.MailboxVersion);
        BE.U32(Mb, L.MbGame, L.GameSb4e);
        BE.U32(Mb, L.MbSizeField, L.MbSize);
        BE.U32(Mb, L.MbMagic1, L.Magic1);
        BE.U32(Mb, L.MbMagic0, L.Magic0);
    }

    public void GameWriteLocal(PlayerState s)
    {
        var slot = Mb.Slice(L.MbLocal, L.PsSize);
        uint seq = BE.U32(slot, L.PsSeq);
        BE.U32(slot, L.PsSeq, seq + 1);
        s.WriteBody(slot);
        BE.U32(slot, L.PsSeq, seq + 2);
    }

    public void GameBeginTornWrite()
    {
        var slot = Mb.Slice(L.MbLocal, L.PsSize);
        BE.U32(slot, L.PsSeq, BE.U32(slot, L.PsSeq) | 1);
    }

    public void GamePushEvent(GameEvent ev)
    {
        int ring = L.MbEventsOut;
        uint head = BE.U32(Mb, ring + L.RingHead);
        (ev with { Id = head + 1 }).Write(Mb.Slice(ring + L.RingEntries + (int)(head % L.EventRingSize) * L.EvSize));
        BE.U32(Mb, ring + L.RingHead, head + 1);
    }

    public List<GameEvent> GamePopEvents()
    {
        var list = new List<GameEvent>();
        int ring = L.MbEventsIn;
        uint head = BE.U32(Mb, ring + L.RingHead), tail = BE.U32(Mb, ring + L.RingTail);
        for (; tail != head; tail++)
            list.Add(GameEvent.Read(Mb.Slice(ring + L.RingEntries + (int)(tail % L.EventRingSize) * L.EvSize)));
        BE.U32(Mb, ring + L.RingTail, tail);
        return list;
    }

    public (uint seq, PlayerState state) GameReadRemote(int slot)
    {
        var s = Mb.Slice(L.RemoteSlot(slot), L.PsSize);
        return (BE.U32(s, L.PsSeq), PlayerState.Read(s));
    }

    public (uint serial, int scenario, string stage) GameReadTravel()
    {
        var t = Mb.Slice(L.MbTravel, L.TrSize);
        return (BE.U32(t, L.TrSerial), BE.S32(t, L.TrScenario), BE.Str(t, L.TrStage, L.StageNameLen));
    }

    /// <summary>What the in-game menu does: choices first, then the request serial.</summary>
    public void GameMenuRequest(MenuRequest request) => request.Write(Mb.Slice(L.MbMenu, L.MnSize));

    public MenuRequest GameReadMenuRequest() => MenuRequest.Read(Mb.Slice(L.MbMenu, L.MnSize));

    public (uint seq, uint handled, MenuStatus status) GameReadMenuStatus()
    {
        var m = Mb.Slice(L.MbMenu, L.MnSize);
        return (BE.U32(m, L.MnStatusSeq), BE.U32(m, L.MnHandledSerial), MenuStatus.Read(m));
    }

    public uint OutTail => BE.U32(Mb, L.MbEventsOut + L.RingTail);
    public uint Heartbeat => BE.U32(Mb, L.MbHeartbeat);
    public void Corrupt() => BE.U32(Mb, L.MbMagic0, 0);
}
