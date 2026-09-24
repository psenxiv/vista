using System.Numerics;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class LiveUpTests
{
    [Fact]
    public void ASnapTurnsAboutTheCurrentUp()
    {
        // The facing turns straight back in one frame, a doubleback: up stays (0, 1, 0), so the picture stays upright.
        Near(Vector3.UnitY, LiveUp.Carry(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void SettlingTowardUprightTurnsAnInvertedPictureBackRound()
    {
        // Level and inverted, settling toward upright: up turns back about the facing at the 180° a second cap, so half
        // a second in it's a quarter turn round, and after a second it's upright.
        var up = -Vector3.UnitY;
        var facing = new Vector3(0f, 0f, -1f);
        for (var step = 0; step < 50; step++)
            (up, _) = LiveUp.SettleToward(up, facing, Vector3.UnitY, 0.01f, 1f);
        Assert.InRange(MathF.Acos(Math.Clamp(Vector3.Dot(up, Vector3.UnitY), -1f, 1f)), 89f * Deg, 91f * Deg);

        for (var step = 0; step < 50; step++)
            (up, _) = LiveUp.SettleToward(up, facing, Vector3.UnitY, 0.01f, 1f);
        Assert.InRange(MathF.Acos(Math.Clamp(Vector3.Dot(up, Vector3.UnitY), -1f, 1f)), 0f, 1f * Deg);
    }
}
