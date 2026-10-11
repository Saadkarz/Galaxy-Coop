using GalaxyCoop.Protocol;

namespace GalaxyCoop.Companion;

/// <summary>
/// Read-only dump of every running Dolphin's mailbox (local player and active
/// remote slots). Unlike attach it never writes, so it can run next to a live session.
/// </summary>
public static class ProbeMode
{
    public static void Run(Options opts)
    {
        int samples = opts.GetInt("samples") ?? 10;
        int interval = opts.GetInt("interval") ?? 100;
        var games = new List<(int pid, DolphinMemory mem, MailboxBridge bridge)>();
        foreach (var p in DolphinMemory.FindDolphinProcesses())
        {
            var mem = DolphinMemory.TryAttach(p.Id, "SB4E", out var error);
            if (mem is null) { Console.WriteLine($"pid {p.Id}: {error}"); continue; }
            var bridge = new MailboxBridge(mem);
            if (bridge.TryLocate()) games.Add((p.Id, mem, bridge));
            else Console.WriteLine($"pid {p.Id}: no mailbox");
        }

        var ghostVtable = opts.Has("ghosts") ? MapOffset("__vt__Q227@unnamed@RemotePlayers_cpp@17RemotePlayerActor") : null;
        var ghosts = games.ToDictionary(g => g.pid, g => ghostVtable is uint vt ? FindGhosts(g.mem, g.bridge, vt) : new List<uint>());

        for (int s = 0; s < samples; s++)
        {
            foreach (var (pid, mem, bridge) in games)
            {
                foreach (var ghost in ghosts[pid])
                    Console.WriteLine($"pid {pid} ghost  {DescribeGhost(mem, ghost)}");
                Console.WriteLine($"pid {pid} local  {Describe(bridge.PeekPlayer(-1))} dbg[{string.Join(' ', bridge.PeekDebug())}]");
                for (int slot = 0; slot < MailboxLayout.MaxRemote; slot++)
                    if (bridge.PeekPlayer(slot) is { Active: true } r)
                        Console.WriteLine($"pid {pid} remote{slot} {Describe(r)}");
            }
            Thread.Sleep(interval);
        }
        foreach (var g in games) g.mem.Dispose();
    }

    /// <summary>Offset of a symbol in the built module, from build/smg2/CustomCode_SB4E.map.</summary>
    static uint? MapOffset(string symbol)
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
        {
            var map = Path.Combine(d.FullName, "build", "smg2", "CustomCode_SB4E.map");
            if (!File.Exists(map)) continue;
            foreach (var line in File.ReadLines(map))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parts[2] == symbol) return Convert.ToUInt32(parts[0], 16);
            }
        }
        return null;
    }

    /// <summary>Ghost actors are found by their vtable pointer, placed relative to where the mailbox landed.</summary>
    static List<uint> FindGhosts(DolphinMemory mem, MailboxBridge bridge, uint vtableOffset)
    {
        var found = new List<uint>();
        if (MapOffset("gMailbox__5GCoop") is not uint mbOffset || bridge.Address is not long mbHost) return found;
        var mbRegion = mem.Regions.First(r => mbHost >= r.HostBase && mbHost < r.HostBase + r.Size);
        uint moduleBase = (uint)(mbRegion.GameBase + (mbHost - mbRegion.HostBase)) - mbOffset;
        uint vt = moduleBase + vtableOffset;
        foreach (var region in mem.Regions)
        {
            var buf = new byte[region.Size];
            if (!mem.Read(region.HostBase, buf)) continue;
            for (int i = 0; i + 4 <= buf.Length; i += 4)
                if (BE.U32(buf, i) == vt) found.Add(region.GameBase + (uint)i);
        }
        Console.WriteLine($"module at 0x{moduleBase:X8}, ghost vtable 0x{vt:X8}, {found.Count} ghosts");
        return found;
    }

    static string DescribeGhost(DolphinMemory mem, uint actor)
    {
        uint U32(uint a) => Peek(mem, a, 4) is { } b ? BE.U32(b, 0) : 0;
        float F32(uint a) => BitConverter.Int32BitsToSingle((int)U32(a));
        string Str(uint a)
        {
            if (a == 0 || Peek(mem, a, 32) is not { } b) return "";
            int n = Array.IndexOf(b, (byte)0);
            return GameText.Decode(System.Text.Encoding.Latin1.GetString(b, 0, n < 0 ? b.Length : n));
        }

        uint manager = U32(actor + 0x54);
        uint player = U32(manager + 0x18);
        uint group = U32(player + 0x60);
        uint ctrl = U32(player + 0x20);
        uint core = U32(player + 0x6C);
        return $"@{actor:X8} pos ({F32(actor + 0xC):F0}, {F32(actor + 0x10):F0}, {F32(actor + 0x14):F0}) player {player:X8} table {U32(player + 0x70):X8} " +
               $"anim '{Str(U32(group))}' f{F32(ctrl + 0x10):0.0} x{F32(ctrl + 0xC):0.00} end {(short)(U32(ctrl + 8) >> 16)} " +
               $"w({F32(player + 0x10):0.00} {F32(player + 0x14):0.00} {F32(player + 0x18):0.00} {F32(player + 0x1C):0.00}) core {core:X8} jointT {U32(core + 0x14):X8}";
    }

    static byte[]? Peek(DolphinMemory mem, uint gameAddress, int length)
    {
        foreach (var r in mem.Regions)
        {
            if (gameAddress < r.GameBase || gameAddress + length > r.GameBase + (uint)r.Size) continue;
            var buf = new byte[length];
            return mem.Read(r.HostBase + (gameAddress - r.GameBase), buf) ? buf : null;
        }
        return null;
    }

    static string Describe(PlayerState? p) => p is not { } s ? "-" :
        $"{(s.Character == 1 ? "Luigi" : "Mario")} {s.Stage}#{s.Scenario} pos {AttachMode.Fmt(s.Pos)} " +
        $"anim '{s.BckNameForDisplay}' f{s.BckFrame:0.0} x{s.BckRate:0.00} " +
        $"w({s.TrackWeights.X:0.00} {s.TrackWeights.Y:0.00} {s.TrackWeights.Z:0.00} {s.TrackWeights.W:0.00}) " +
        (s.UpperName.Length > 0 ? $"upper '{GameText.Decode(s.UpperName)}' f{s.UpperFrame:0.0} x{s.UpperRate:0.00} " : "") +
        $"[{s.Flags}]";
}
