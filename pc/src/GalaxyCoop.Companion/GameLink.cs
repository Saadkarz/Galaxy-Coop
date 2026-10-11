using GalaxyCoop.Protocol;

namespace GalaxyCoop.Companion;

/// <summary>
/// Keeps a mailbox connection to one Dolphin instance alive: attaches, locates
/// the mailbox, and transparently re-attaches after a game reset or restart.
/// </summary>
public sealed class GameLink : IDisposable
{
    readonly int? _pid;
    readonly Func<IGameMemory?>? _memoryFactory;
    IGameMemory? _memory;
    DateTime _nextAttempt = DateTime.MinValue;
    string _lastStatus = "";

    public MailboxBridge? Bridge { get; private set; }
    public event Action<string>? Status;
    public event Action? Attached;
    public event Action? Detached;

    public GameLink(int? pid) => _pid = pid;

    /// <summary>For tests: supply the memory source directly.</summary>
    public GameLink(Func<IGameMemory?> memoryFactory) => _memoryFactory = memoryFactory;

    public bool IsReady => Bridge?.Address is not null;

    /// <summary>Polls the mailbox, attaching first if needed. Returns null while not attached.</summary>
    public MailboxSnapshot? Poll()
    {
        if (!IsReady && !TryAttach()) return null;
        var snap = Bridge!.Poll();
        if (snap is null)
        {
            Report("mailbox lost (game reset or closed); re-attaching");
            Drop();
        }
        return snap;
    }

    bool TryAttach()
    {
        if (DateTime.UtcNow < _nextAttempt) return false;
        _nextAttempt = DateTime.UtcNow.AddSeconds(1);

        if (_memory is null || !_memory.IsAlive)
        {
            _memory?.Dispose();
            _memory = null;
            if (_memoryFactory is not null)
                _memory = _memoryFactory();
            else
            {
                var dolphin = DolphinMemory.TryAttach(_pid, "SB4E", out var error);
                if (dolphin is null)
                {
                    Report(error);
                    return false;
                }
                Report($"attached to Dolphin pid {dolphin.ProcessId}, game {dolphin.GameId}");
                _memory = dolphin;
            }
            if (_memory is null) return false;
            Bridge = new MailboxBridge(_memory);
        }

        if (!Bridge!.TryLocate())
        {
            Report("game found but no mailbox yet (is the GalaxyCoop Riivolution patch enabled? enter a level)");
            return false;
        }
        Report($"mailbox at {Bridge.AddressDescription}");
        Bridge.ClearAllRemotes();
        Attached?.Invoke();
        return true;
    }

    void Drop()
    {
        Bridge = _memory is null ? null : new MailboxBridge(_memory);
        Detached?.Invoke();
    }

    void Report(string message)
    {
        if (message == _lastStatus) return;
        _lastStatus = message;
        Status?.Invoke(message);
    }

    public void Dispose()
    {
        if (IsReady)
        {
            Bridge!.ClearAllRemotes();
            Bridge.WriteHeartbeat(CompanionFlags.None);
        }
        _memory?.Dispose();
    }
}
