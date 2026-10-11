using System.Numerics;

namespace GalaxyCoop.Protocol.Net;

/// <summary>
/// Plays a remote player back slightly in the past so there are always two
/// snapshots to blend between. Sender timestamps are mapped onto the local
/// clock with the smallest observed (local - sender) difference, which is the
/// lowest-latency delivery seen so far and therefore immune to jitter.
/// </summary>
public sealed class SnapshotInterpolator
{
    readonly List<(long t, PlayerState s)> _samples = new();
    long? _offset;
    bool _hasTime;
    uint _lastRaw;
    long _lastTime;

    public int DelayMs { get; init; } = 100;
    public int MaxExtrapolateMs { get; init; } = 150;
    public float TeleportDistance { get; init; } = 1500f;
    public int Capacity { get; init; } = 64;
    /// <summary>Game frames per second; snapshot velocities are in units per frame.</summary>
    public float FrameRate { get; init; } = 60f;

    public int Count => _samples.Count;
    public long? LatestSenderTime => _samples.Count > 0 ? _samples[^1].t : null;

    public void Add(uint senderTimeMs, PlayerState state, long localNowMs)
    {
        long t = Unwrap(senderTimeMs);
        long offset = localNowMs - t;
        if (_offset is null || offset < _offset) _offset = offset;

        int i = _samples.Count;
        while (i > 0 && _samples[i - 1].t > t) i--;
        if (i > 0 && _samples[i - 1].t == t) return;
        _samples.Insert(i, (t, state));
        if (_samples.Count > Capacity) _samples.RemoveAt(0);
    }

    long Unwrap(uint raw)
    {
        if (!_hasTime)
        {
            _hasTime = true;
            _lastRaw = raw;
            _lastTime = raw;
            return raw;
        }
        long t = _lastTime + unchecked((int)(raw - _lastRaw));
        if (t > _lastTime)
        {
            _lastTime = t;
            _lastRaw = raw;
        }
        return t;
    }

    public PlayerState? Sample(long localNowMs)
    {
        if (_samples.Count == 0 || _offset is not long offset) return null;
        long target = localNowMs - offset - DelayMs;

        int b = _samples.FindIndex(x => x.t > target);
        if (b == 0) return _samples[0].s;
        if (b < 0)
        {
            var (lt, last) = _samples[^1];
            float dt = Math.Min(target - lt, MaxExtrapolateMs) / 1000f * FrameRate;
            return last with
            {
                Pos = last.Pos + last.Vel * dt,
                BckFrame = last.BckFrame + last.BckRate * dt,
                UpperFrame = last.UpperFrame + last.UpperRate * dt,
            };
        }

        var (ta, a) = _samples[b - 1];
        var (tb, s) = _samples[b];
        while (_samples.Count > 2 && _samples[1].t <= target) _samples.RemoveAt(0);

        if (a.Stage != s.Stage || Vector3.Distance(a.Pos, s.Pos) > TeleportDistance) return a;
        float alpha = (float)(target - ta) / (tb - ta);
        // The animation keeps playing between snapshots; the game wraps looping frames.
        float framesSinceA = (target - ta) / 1000f * FrameRate;
        bool sameAnim = a.BckName == s.BckName;
        return a with
        {
            Pos = Vector3.Lerp(a.Pos, s.Pos, alpha),
            Vel = Vector3.Lerp(a.Vel, s.Vel, alpha),
            Gravity = Nlerp(a.Gravity, s.Gravity, alpha),
            Up = Nlerp(a.Up, s.Up, alpha),
            Front = Nlerp(a.Front, s.Front, alpha),
            BckFrame = a.BckFrame + a.BckRate * framesSinceA,
            BckRate = sameAnim ? a.BckRate + (s.BckRate - a.BckRate) * alpha : a.BckRate,
            TrackWeights = sameAnim ? Vector4.Lerp(a.TrackWeights, s.TrackWeights, alpha) : a.TrackWeights,
            UpperFrame = a.UpperFrame + a.UpperRate * framesSinceA,
        };
    }

    static Vector3 Nlerp(Vector3 a, Vector3 b, float t)
    {
        var v = Vector3.Lerp(a, b, t);
        float len = v.Length();
        return len > 1e-4f ? v / len : (t < 0.5f ? a : b);
    }

    public void Clear()
    {
        _samples.Clear();
        _offset = null;
        _hasTime = false;
    }
}
