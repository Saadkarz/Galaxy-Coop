namespace GalaxyCoop.Companion;

/// <param name="HostBase">Address of the region in the companion's view (a Dolphin process address, or a fake buffer offset).</param>
/// <param name="GameBase">Matching emulated address, used only for logging.</param>
public readonly record struct MemoryRegion(long HostBase, uint GameBase, int Size, string Name);

/// <summary>Raw access to emulated console RAM.</summary>
public interface IGameMemory : IDisposable
{
    IReadOnlyList<MemoryRegion> Regions { get; }
    bool Read(long hostAddress, Span<byte> destination);
    bool Write(long hostAddress, ReadOnlySpan<byte> source);
    bool IsAlive { get; }
}
