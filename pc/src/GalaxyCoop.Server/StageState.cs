using GalaxyCoop.Protocol;

namespace GalaxyCoop.Server;

/// <summary>
/// Authoritative shared state of one (stage, scenario). The server, not any
/// one player, decides which event wins, so two players grabbing the same coin
/// on the same frame still produce exactly one "collected" broadcast.
/// </summary>
public sealed class StageState
{
    readonly HashSet<(GameEventType, uint)> _taken = new();
    readonly Dictionary<uint, (bool on, byte source, GameEvent ev)> _switches = new();
    readonly List<(byte source, GameEvent ev)> _log = new();
    bool _comet;
    byte _cometSource;
    GameEvent? _cometEvent;

    public int TakenCount => _taken.Count;

    public static bool IsOneShot(GameEventType t) =>
        t is GameEventType.Coin or GameEventType.StarPiece or GameEventType.PowerStar or GameEventType.EnemyKill;

    /// <summary>Returns true if the event changes the world and must be broadcast.</summary>
    public bool TryApply(byte source, GameEvent ev)
    {
        switch (ev.Type)
        {
            case var t when IsOneShot(t):
                if (!_taken.Add((t, ev.Key))) return false;
                _log.Add((source, ev));
                return true;
            case GameEventType.SwitchOn:
            case GameEventType.SwitchOff:
            {
                bool on = ev.Type == GameEventType.SwitchOn;
                if (_switches.TryGetValue(ev.Key, out var cur) && cur.on == on) return false;
                if (!on && !_switches.ContainsKey(ev.Key)) return false;
                _switches[ev.Key] = (on, source, ev);
                return true;
            }
            case GameEventType.PlayerDeath:
                return true;
            case GameEventType.CometOn:
            case GameEventType.CometOff:
            {
                bool on = ev.Type == GameEventType.CometOn;
                if (_comet == on) return false;
                _comet = on;
                _cometSource = source;
                _cometEvent = ev;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Everything a player entering the stage must apply to catch up.</summary>
    public IEnumerable<(byte source, GameEvent ev)> Replay()
    {
        foreach (var e in _log) yield return e;
        foreach (var s in _switches.Values)
            if (s.on) yield return (s.source, s.ev);
        if (_comet && _cometEvent is { } comet) yield return (_cometSource, comet);
    }
}
