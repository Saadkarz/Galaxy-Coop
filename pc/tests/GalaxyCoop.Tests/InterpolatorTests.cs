using System.Numerics;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Tests;

public class InterpolatorTests
{
    static PlayerState At(float x, string stage = "A", float vx = 0) => new()
    {
        Pos = new Vector3(x, 0, 0), Vel = new Vector3(vx, 0, 0), Up = Vector3.UnitY, Front = Vector3.UnitZ, Stage = stage,
    };

    [Fact]
    public void BlendsBetweenSnapshotsAfterDelay()
    {
        var it = new SnapshotInterpolator { DelayMs = 100 };
        // Sender clock starts at 5000; arrives with 20 ms latency on a local clock starting at 0.
        it.Add(5000, At(0), 20);
        it.Add(5033, At(33), 53);
        it.Add(5066, At(66), 86);
        var s = it.Sample(136)!; // local 136 -> sender 116 -> minus delay = 16
        Assert.Equal(16f, s.Pos.X, 3);
    }

    [Fact]
    public void JitterDoesNotMoveTheTimeline()
    {
        var it = new SnapshotInterpolator { DelayMs = 100 };
        it.Add(1000, At(0), 10);
        it.Add(1033, At(33), 120); // arrived late
        it.Add(1066, At(66), 76);
        Assert.Equal(16f, it.Sample(126)!.Pos.X, 3);
    }

    [Fact]
    public void ExtrapolatesWithVelocityButOnlyBriefly()
    {
        var it = new SnapshotInterpolator { DelayMs = 0, MaxExtrapolateMs = 100, FrameRate = 60 };
        it.Add(0, At(0, vx: 1), 0);
        Assert.Equal(3f, it.Sample(50)!.Pos.X, 3);
        Assert.Equal(6f, it.Sample(5000)!.Pos.X, 3);
    }

    [Fact]
    public void SnapsOnTeleportOrStageChange()
    {
        var it = new SnapshotInterpolator { DelayMs = 0, TeleportDistance = 500 };
        it.Add(0, At(0), 0);
        it.Add(100, At(10_000), 100);
        Assert.Equal(0f, it.Sample(50)!.Pos.X);

        var st = new SnapshotInterpolator { DelayMs = 0 };
        st.Add(0, At(0, "A"), 0);
        st.Add(100, At(10, "B"), 100);
        Assert.Equal("A", st.Sample(50)!.Stage);
        Assert.Equal("B", st.Sample(100)!.Stage);
    }

    [Fact]
    public void SurvivesSenderClockWrap()
    {
        var it = new SnapshotInterpolator { DelayMs = 0 };
        it.Add(0xFFFFFFF0, At(0), 1000);
        it.Add(0x00000010, At(32), 1032);
        Assert.Equal(16f, it.Sample(1016)!.Pos.X, 3);
    }
}
