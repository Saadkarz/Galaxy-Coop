using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GalaxyCoop.Companion;

/// <summary>
/// Reads and writes the emulated RAM of a running Dolphin process.
/// Dolphin backs MEM1 and MEM2 with file mappings; we find them the same way
/// Dolphin Memory Engine does, by walking the process's committed MEM_MAPPED
/// regions and matching their sizes, then confirm MEM1 by the disc ID that the
/// game keeps at 0x80000000.
/// </summary>
public sealed class DolphinMemory : IGameMemory
{
    const uint Mem1Size = 0x01800000;
    const uint Mem1MappingSize = 0x02000000;
    const uint Mem2Size = 0x04000000;

    readonly Process _process;
    readonly IntPtr _handle;
    public IReadOnlyList<MemoryRegion> Regions { get; }
    public int ProcessId => _process.Id;
    public string GameId { get; }

    DolphinMemory(Process process, IntPtr handle, List<MemoryRegion> regions, string gameId)
    {
        _process = process;
        _handle = handle;
        Regions = regions;
        GameId = gameId;
    }

    public bool IsAlive
    {
        get
        {
            try { return !_process.HasExited; }
            catch { return false; }
        }
    }

    public static IReadOnlyList<Process> FindDolphinProcesses() =>
        Process.GetProcesses()
            .Where(p => p.ProcessName.StartsWith("Dolphin", StringComparison.OrdinalIgnoreCase)
                        && !p.ProcessName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Id)
            .ToList();

    public static DolphinMemory? TryAttach(int? pid, string expectedGamePrefix, out string error)
    {
        var candidates = pid is int id
            ? new List<Process> { Process.GetProcessById(id) }
            : FindDolphinProcesses().ToList();
        if (candidates.Count == 0)
        {
            error = "no Dolphin process found";
            return null;
        }

        error = "";
        foreach (var process in candidates)
        {
            var handle = Native.OpenProcess(Native.ProcessAccess, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                error = $"cannot open Dolphin process {process.Id} (error {Marshal.GetLastWin32Error()})";
                continue;
            }

            var (mem1, mem2) = FindRamRegions(handle, expectedGamePrefix);
            if (mem1 is null)
            {
                Native.CloseHandle(handle);
                error = $"Dolphin {process.Id}: no emulated RAM with game ID {expectedGamePrefix}* (is the game running?)";
                continue;
            }

            var regions = new List<MemoryRegion> { mem1.Value };
            if (mem2 is not null) regions.Add(mem2.Value);
            var idBytes = new byte[6];
            ReadRaw(handle, mem1.Value.HostBase, idBytes);
            return new DolphinMemory(process, handle, regions, Encoding.ASCII.GetString(idBytes));
        }
        return null;
    }

    static (MemoryRegion? mem1, MemoryRegion? mem2) FindRamRegions(IntPtr handle, string gamePrefix)
    {
        MemoryRegion? mem1 = null, mem2 = null;
        long address = 0;
        var info = new Native.MEMORY_BASIC_INFORMATION();
        var size = (IntPtr)Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>();
        var id = new byte[4];
        while (Native.VirtualQueryEx(handle, (IntPtr)address, out info, size) != IntPtr.Zero)
        {
            long regionSize = (long)info.RegionSize;
            if (info.State == Native.MEM_COMMIT && info.Type == Native.MEM_MAPPED)
            {
                long b = (long)info.BaseAddress;
                if (mem1 is null && (regionSize == Mem1MappingSize || regionSize == Mem1Size))
                {
                    if (ReadRaw(handle, b, id) && Encoding.ASCII.GetString(id).StartsWith(gamePrefix, StringComparison.Ordinal))
                        mem1 = new MemoryRegion(b, 0x80000000, (int)Mem1Size, "MEM1");
                }
                else if (mem2 is null && regionSize == Mem2Size && mem1 is not null)
                {
                    mem2 = new MemoryRegion(b, 0x90000000, (int)Mem2Size, "MEM2");
                }
            }
            long next = (long)info.BaseAddress + regionSize;
            if (next <= address) break;
            address = next;
        }
        return (mem1, mem2);
    }

    static bool ReadRaw(IntPtr handle, long address, byte[] buffer) =>
        Native.ReadProcessMemory(handle, (IntPtr)address, buffer, buffer.Length, out var read) && read == buffer.Length;

    public bool Read(long hostAddress, Span<byte> destination)
    {
        var buffer = new byte[destination.Length];
        if (!Native.ReadProcessMemory(_handle, (IntPtr)hostAddress, buffer, buffer.Length, out var read) || read != buffer.Length)
            return false;
        buffer.CopyTo(destination);
        return true;
    }

    public bool Write(long hostAddress, ReadOnlySpan<byte> source)
    {
        var buffer = source.ToArray();
        return Native.WriteProcessMemory(_handle, (IntPtr)hostAddress, buffer, buffer.Length, out var written) && written == buffer.Length;
    }

    public void Dispose() => Native.CloseHandle(_handle);

    static class Native
    {
        public const uint PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020, PROCESS_VM_OPERATION = 0x0008, PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint ProcessAccess = PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;
        public const uint MEM_COMMIT = 0x1000, MEM_MAPPED = 0x40000;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int written);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, IntPtr length);
    }
}
