using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class LiveUpTests
{
    [Fact]
    public void ASnapTurnsAboutTheCurrentUp()
    {
        // The facing turns straight back along x in one frame, a doubleback: up stays (0, 1, 0), so the picture stays
        // upright, where half a turn about any axis square to x would turn it over.
        Near(Vector3.UnitY, LiveUp.Carry(Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void CarryingKeepsTheRollThroughAPitch()
    {
        // Pitching 60° up from along -z turns about +x; up rolled 45°, (√½, √½, 0), turns with it:
        // (√½, √½ cos 60°, √½ sin 60°) = (0.70711, 0.35355, 0.61237).
        var up = LiveUp.Carry(
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, MathF.Sin(60f * Deg), -MathF.Cos(60f * Deg)),
            new Vector3(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f), 0f)
        );

        Near(new Vector3(0.70711f, 0.35355f, 0.61237f), up, 1e-5f);
    }

    [Fact]
    public void SettlingFadesNearStraightUp()
    {
        // Facing 70° up along -z, the level part is sin 20° = 0.34202; settling fades in from sin 15° = 0.25882 to sin 30°
        // = 0.5, so it runs at (0.34202 - 0.25882) / (0.5 - 0.25882) = 0.34497 of 180° a second: 0.62094° in 10 ms.
        var facing = new Vector3(0f, MathF.Sin(70f * Deg), -MathF.Cos(70f * Deg));
        var upright = new Vector3(0f, MathF.Cos(70f * Deg), MathF.Sin(70f * Deg));
        var rolled = Vector3.Transform(upright, Quaternion.CreateFromAxisAngle(facing, 10f * Deg));

        var (up, _) = LiveUp.SettleToward(rolled, facing, upright, 0.01f, 1f);

        Assert.Equal(10f - 0.62094f, Vectors.AngleBetween(up, upright) / Deg, 1e-3f);
    }

    [Fact]
    public void NearAHalfTurnSettlingKeepsTurningTheWayItWent()
    {
        // Up is 175° round from upright about the facing, so the short way is back through 175°; having last turned the
        // other way (-1), it keeps going that way, the long 185°, and the way it returns is still -1.
        var facing = new Vector3(0f, 0f, -1f);
        var up = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(facing, -175f * Deg));

        var (settled, way) = LiveUp.SettleToward(up, facing, Vector3.UnitY, 0.01f, -1f);

        // 1.8° further the long way: -176.8° round from upright.
        Near(Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(facing, -176.8f * Deg)), settled, 1e-5f);
        Assert.Equal(-1f, way);
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
        Assert.InRange(Vectors.AngleBetween(up, Vector3.UnitY), 89f * Deg, 91f * Deg);

        for (var step = 0; step < 50; step++)
            (up, _) = LiveUp.SettleToward(up, facing, Vector3.UnitY, 0.01f, 1f);
        Assert.InRange(Vectors.AngleBetween(up, Vector3.UnitY), 0f, 1f * Deg);
    }
}
