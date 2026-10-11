using GalaxyCoop.Protocol;

namespace GalaxyCoop.Companion;

/// <summary>
/// Feeds the local player's own state back into remote slot 0 after a delay and
/// with a positional offset, so one Dolphin instance can verify the ghost actor
/// (model, animation names, gravity orientation) without any networking.
/// </summary>
public static class LoopbackMode
{
    public static async Task RunAsync(Options opts, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(opts.GetInt("delay-ms") ?? 400);
        var offset = opts.GetVec("offset", new System.Numerics.Vector3(0, 0, 0));
        using var link = new GameLink(opts.GetInt("pid"));
        link.Status += s => Console.WriteLine($"[loopback] {s}");

        var buffer = new Queue<(DateTime at, PlayerState state)>();
        var lastPrint = DateTime.MinValue;
        Console.WriteLine($"[loopback] delay {delay.TotalMilliseconds} ms, offset {AttachMode.Fmt(offset)}");

        while (!ct.IsCancellationRequested)
        {
            var snap = link.Poll();
            if (snap is not null)
            {
                var bridge = link.Bridge!;
                bridge.WriteHeartbeat(CompanionFlags.Attached | CompanionFlags.Connected);
                var now = DateTime.UtcNow;
                if (snap.Local is PlayerState local)
                    buffer.Enqueue((now, local));

                PlayerState? due = null;
                while (buffer.Count > 0 && now - buffer.Peek().at >= delay)
                    due = buffer.Dequeue().state;

                if (due is not null)
                {
                    var echo = due with
                    {
                        Pos = due.Pos + Offset(due, offset),
                        Active = due.InScene && snap.Local?.Stage == due.Stage,
                        PlayerId = 1,
                        Character = (byte)(due.Character ^ 1),
                    };
                    bridge.WriteRemote(0, echo);
                }

                foreach (var ev in snap.OutgoingEvents)
                    Console.WriteLine($"[event] {ev.Type} key={ev.Key:X8} arg0={ev.Arg0} '{ev.Payload}'");
                if (now - lastPrint > TimeSpan.FromSeconds(1))
                {
                    lastPrint = now;
                    Console.WriteLine(AttachMode.Describe(snap));
                }
            }
            await Task.Delay(8, ct);
        }
    }

    /// <summary>Applies the offset in the player's local frame (X = side, Y = up, Z = front) so it works on any planet.</summary>
    static System.Numerics.Vector3 Offset(PlayerState s, System.Numerics.Vector3 o)
    {
        if (o == default) return default;
        var up = s.Up.LengthSquared() > 0.5f ? System.Numerics.Vector3.Normalize(s.Up) : System.Numerics.Vector3.UnitY;
        var front = s.Front.LengthSquared() > 0.5f ? System.Numerics.Vector3.Normalize(s.Front) : System.Numerics.Vector3.UnitZ;
        var side = System.Numerics.Vector3.Cross(up, front);
        return side * o.X + up * o.Y + front * o.Z;
    }
}
