using GalaxyCoop.Protocol;

namespace GalaxyCoop.Companion;

public static class AttachMode
{
    public static async Task RunAsync(Options opts, CancellationToken ct)
    {
        using var link = new GameLink(opts.GetInt("pid"));
        link.Status += s => Console.WriteLine($"[attach] {s}");
        var lastPrint = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            var snap = link.Poll();
            if (snap is not null)
            {
                link.Bridge!.WriteHeartbeat(CompanionFlags.Attached);
                foreach (var ev in snap.OutgoingEvents)
                    Console.WriteLine($"[event] #{ev.Id} {ev.Type} key={ev.Key:X8} arg0={ev.Arg0} arg1={ev.Arg1} pos={Fmt(ev.Pos)} '{ev.Payload}'");

                if (DateTime.UtcNow - lastPrint > TimeSpan.FromMilliseconds(250))
                {
                    lastPrint = DateTime.UtcNow;
                    Console.WriteLine(Describe(snap));
                }
            }
            await Task.Delay(8, ct);
        }
    }

    public static string Describe(MailboxSnapshot snap)
    {
        var dbg = string.Join(' ', snap.Debug);
        if (snap.Local is not PlayerState p)
            return $"frame {snap.GameFrame} scene {snap.SceneSerial} (state being written) dbg[{dbg}]";
        return $"frame {snap.GameFrame} scene {snap.SceneSerial} {p.Stage}#{p.Scenario} " +
               $"pos {Fmt(p.Pos)} vel {Fmt(p.Vel)} grav {Fmt(p.Gravity)} " +
               $"anim '{p.BckNameForDisplay}' f{p.BckFrame:0.0} x{p.BckRate:0.00} w({p.TrackWeights.X:0.00} {p.TrackWeights.Y:0.00} {p.TrackWeights.Z:0.00} {p.TrackWeights.W:0.00}) life {p.Life} flags [{p.Flags}] dbg[{dbg}]";
    }

    public static string Fmt(System.Numerics.Vector3 v) => $"({v.X:F1}, {v.Y:F1}, {v.Z:F1})";
}
