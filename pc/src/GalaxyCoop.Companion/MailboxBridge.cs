using System.Numerics;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;
using L = GalaxyCoop.Protocol.MailboxLayout;

namespace GalaxyCoop.Companion;

public sealed record MailboxSnapshot(
    uint GameFrame,
    uint SceneSerial,
    PlayerState? Local,
    IReadOnlyList<GameEvent> OutgoingEvents,
    uint[] Debug,
    MenuRequest? Menu = null);

/// <summary>
/// Companion side of the mailbox protocol: locates the block in emulated RAM
/// and exchanges player states and events with the game module.
/// </summary>
public sealed class MailboxBridge
{
    const int ScanChunk = 1 << 20;
    static readonly byte[] MagicBytes = { 0x47, 0x43, 0x4F, 0x4F, 0x50, 0x4D, 0x42, 0x31 };

    readonly IGameMemory _memory;
    readonly byte[] _buffer = new byte[L.MbSize];

    public long? Address { get; private set; }
    public string AddressDescription { get; private set; } = "";
    public uint Heartbeat { get; private set; }

    public MailboxBridge(IGameMemory memory) => _memory = memory;

    public bool TryLocate()
    {
        Address = null;
        var chunk = new byte[ScanChunk + MagicBytes.Length];
        foreach (var region in _memory.Regions)
        {
            for (long off = 0; off < region.Size; off += ScanChunk)
            {
                int len = (int)Math.Min(chunk.Length, region.Size - off);
                var span = chunk.AsSpan(0, len);
                if (!_memory.Read(region.HostBase + off, span)) continue;
                for (int i = 0; i + MagicBytes.Length <= len; i += 4)
                {
                    if (span[i] != MagicBytes[0] || !span.Slice(i, MagicBytes.Length).SequenceEqual(MagicBytes)) continue;
                    long candidate = region.HostBase + off + i;
                    if (Validate(candidate))
                    {
                        Address = candidate;
                        AddressDescription = $"{region.Name} 0x{region.GameBase + (uint)(off + i):X8}";
                        return true;
                    }
                }
            }
        }
        return false;
    }

    bool Validate(long address)
    {
        Span<byte> header = stackalloc byte[0x20];
        if (!_memory.Read(address, header)) return false;
        return BE.U32(header, L.MbMagic0) == L.Magic0
               && BE.U32(header, L.MbMagic1) == L.Magic1
               && BE.U32(header, L.MbVersion) == L.MailboxVersion
               && BE.U32(header, L.MbGame) == L.GameSb4e
               && BE.U32(header, L.MbSizeField) == L.MbSize;
    }

    /// <summary>Reads the whole block; returns null if the mailbox vanished (game reset or closed).</summary>
    public MailboxSnapshot? Poll()
    {
        if (Address is not long a || !_memory.Read(a, _buffer)) return null;
        var b = _buffer.AsSpan();
        if (BE.U32(b, L.MbMagic0) != L.Magic0 || BE.U32(b, L.MbMagic1) != L.Magic1) return null;

        PlayerState? local = null;
        uint seq = BE.U32(b, L.MbLocal + L.PsSeq);
        if ((seq & 1) == 0)
        {
            var candidate = PlayerState.Read(b.Slice(L.MbLocal, L.PsSize));
            Span<byte> seqNow = stackalloc byte[4];
            if (_memory.Read(a + L.MbLocal + L.PsSeq, seqNow) && BE.U32(seqNow, 0) == seq)
                local = candidate;
        }

        var events = new List<GameEvent>();
        int ring = L.MbEventsOut;
        uint head = BE.U32(b, ring + L.RingHead);
        uint tail = BE.U32(b, ring + L.RingTail);
        if (head - tail > L.EventRingSize) tail = head - L.EventRingSize;
        for (uint i = tail; i != head; i++)
        {
            int entry = ring + L.RingEntries + (int)(i % L.EventRingSize) * L.EvSize;
            events.Add(GameEvent.Read(b.Slice(entry, L.EvSize)));
        }
        if (events.Count > 0) WriteU32(a + ring + L.RingTail, head);

        var debug = new uint[6];
        for (int i = 0; i < debug.Length; i++) debug[i] = BE.U32(b, L.MbDebug + i * 4);

        return new MailboxSnapshot(BE.U32(b, L.MbGameFrame), BE.U32(b, L.MbSceneSerial), local, events, debug,
            MenuRequest.Read(b.Slice(L.MbMenu, L.MnSize)));
    }

    /// <summary>Publishes the companion's half of the menu block under its seqlock.</summary>
    public bool WriteMenuStatus(MenuStatus status, uint handledSerial)
    {
        if (Address is not long a) return false;
        long menu = a + L.MbMenu;
        Span<byte> seqBuf = stackalloc byte[4];
        if (!_memory.Read(menu + L.MnStatusSeq, seqBuf)) return false;
        uint seq = BE.U32(seqBuf, 0) | 1;
        WriteU32(menu + L.MnStatusSeq, seq);
        var block = new byte[L.MnSize];
        status.Write(block);
        BE.U32(block, L.MnHandledSerial, handledSerial);
        _memory.Write(menu + L.MnHandledSerial, block.AsSpan(L.MnHandledSerial));
        WriteU32(menu + L.MnStatusSeq, seq + 1);
        return true;
    }

    /// <summary>Reads a player block without touching anything; slot -1 is the local player.</summary>
    public PlayerState? PeekPlayer(int slot)
    {
        if (Address is not long a) return null;
        var buf = new byte[L.PsSize];
        return _memory.Read(a + (slot < 0 ? L.MbLocal : L.RemoteSlot(slot)), buf) ? PlayerState.Read(buf) : null;
    }

    public uint[] PeekDebug()
    {
        var buf = new byte[6 * 4];
        var debug = new uint[6];
        if (Address is long a && _memory.Read(a + L.MbDebug, buf))
            for (int i = 0; i < debug.Length; i++) debug[i] = BE.U32(buf, i * 4);
        return debug;
    }

    public void WriteHeartbeat(CompanionFlags flags)
    {
        if (Address is not long a) return;
        Heartbeat++;
        WriteU32(a + L.MbHeartbeat, Heartbeat);
        WriteU32(a + L.MbCompanionFlags, (uint)flags);
    }

    /// <summary>Writes a remote slot under the seqlock: odd sequence, body, even sequence.</summary>
    public bool WriteRemote(int slot, PlayerState state)
    {
        if (Address is not long a || slot < 0 || slot >= L.MaxRemote) return false;
        long baseAddr = a + L.RemoteSlot(slot);
        Span<byte> seqBuf = stackalloc byte[4];
        if (!_memory.Read(baseAddr + L.PsSeq, seqBuf)) return false;
        uint seq = BE.U32(seqBuf, 0) | 1;
        WriteU32(baseAddr + L.PsSeq, seq);
        var body = new byte[L.PsSize];
        state.WriteBody(body);
        _memory.Write(baseAddr + L.PsFrame, body.AsSpan(L.PsFrame));
        WriteU32(baseAddr + L.PsSeq, seq + 1);
        return true;
    }

    public void ClearRemote(int slot) => WriteRemote(slot, new PlayerState { Active = false });

    public void ClearAllRemotes()
    {
        for (int i = 0; i < L.MaxRemote; i++) ClearRemote(i);
    }

    /// <summary>Queues an event for the game. Returns false if the ring is full.</summary>
    public bool PushIncomingEvent(GameEvent ev)
    {
        if (Address is not long a) return false;
        int ring = L.MbEventsIn;
        Span<byte> ht = stackalloc byte[8];
        if (!_memory.Read(a + ring, ht)) return false;
        uint head = BE.U32(ht, L.RingHead), tail = BE.U32(ht, L.RingTail);
        if (head - tail >= L.EventRingSize) return false;
        var entry = new byte[L.EvSize];
        (ev with { Id = head + 1 }).Write(entry);
        _memory.Write(a + ring + L.RingEntries + (head % L.EventRingSize) * L.EvSize, entry);
        WriteU32(a + ring + L.RingHead, head + 1);
        return true;
    }

    public readonly record struct SocialOut(uint NameSerial, string Name, uint ChatSerial, string Chat);
    public readonly record struct BossOut(uint Key, Vector3 Pos, Vector3 Rot, bool Active);

    /// <summary>The name and chat the game wrote. Reads the poll buffer, so call it after <see cref="Poll"/>.</summary>
    public SocialOut ReadSocial()
    {
        var b = _buffer.AsSpan(L.MbSocial, L.SoSize);
        return new(BE.U32(b, L.SoNameSerial), BE.Str(b, L.SoName, L.NameLen),
            BE.U32(b, L.SoChatSerial), BE.Str(b, L.SoChat, L.ChatLen));
    }

    /// <summary>Boss pose the game is publishing. A half-written block (odd seqlock) counts as inactive.</summary>
    public BossOut ReadBoss()
    {
        var b = _buffer.AsSpan(L.MbBossOut, L.BsSize);
        uint seq = BE.U32(b, L.BsSeq);
        if ((seq & 1) != 0 || b[L.BsActive] == 0) return default;
        return new(BE.U32(b, L.BsKey), BE.Vec(b, L.BsPos), BE.Vec(b, L.BsRot), true);
    }

    public void WriteBossIn(BossOut boss)
    {
        if (Address is not long a) return;
        long at = a + L.MbBossIn;
        Span<byte> seqBuf = stackalloc byte[4];
        if (!_memory.Read(at + L.BsSeq, seqBuf)) return;
        uint seq = BE.U32(seqBuf, 0) | 1;
        WriteU32(at + L.BsSeq, seq);
        var block = new byte[L.BsSize];
        BE.U32(block, L.BsKey, boss.Key);
        BE.Vec(block, L.BsPos, boss.Pos);
        BE.Vec(block, L.BsRot, boss.Rot);
        block[L.BsActive] = (byte)(boss.Active ? 1 : 0);
        _memory.Write(at + L.BsKey, block.AsSpan(L.BsKey));
        WriteU32(at + L.BsSeq, seq + 1);
    }

    /// <summary>Preset name, chat log and the name tag for each remote slot. The game keeps its own name and outgoing chat.</summary>
    public void WriteSocialIn(string preset, IReadOnlyList<Chat> lines, IReadOnlyList<string?> remoteNames)
    {
        if (Address is not long a) return;
        long at = a + L.MbSocial;
        Span<byte> seqBuf = stackalloc byte[4];
        if (!_memory.Read(at + L.SoInSeq, seqBuf)) return;
        uint seq = BE.U32(seqBuf, 0) | 1;
        WriteU32(at + L.SoInSeq, seq);
        var block = new byte[L.SoSize];
        BE.Str(block, L.SoPreset, L.NameLen, preset);
        int n = Math.Min(lines.Count, L.ChatLines);
        BE.U32(block, L.SoLineCount, (uint)n);
        for (int i = 0; i < n; i++)
        {
            int o = L.SoLines + i * L.SoLineSize;
            BE.Str(block, o, L.NameLen, lines[i].Name);
            BE.Str(block, o + L.NameLen, L.ChatLen, lines[i].Text);
        }
        for (int i = 0; i < Math.Min(remoteNames.Count, L.MaxRemote); i++)
            BE.Str(block, L.SoNames + i * L.NameLen, L.NameLen, remoteNames[i]);
        _memory.Write(at + L.SoPreset, block.AsSpan(L.SoPreset, L.SoSize - L.SoPreset));
        WriteU32(at + L.SoInSeq, seq + 1);
    }

    /// <summary>Asks the game to change galaxy: stage and scenario first, then the serial the game watches.</summary>
    public bool RequestTravel(string stage, int scenario)
    {
        if (Address is not long a) return false;
        long t = a + L.MbTravel;
        var block = new byte[L.TrSize];
        if (!_memory.Read(t, block)) return false;
        uint serial = BE.U32(block, L.TrSerial);
        BE.S32(block, L.TrScenario, scenario);
        BE.Str(block, L.TrStage, L.StageNameLen, stage);
        _memory.Write(t + L.TrScenario, block.AsSpan(L.TrScenario));
        WriteU32(t + L.TrSerial, serial + 1);
        return true;
    }

    void WriteU32(long address, uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BE.U32(tmp, 0, value);
        _memory.Write(address, tmp);
    }
}
