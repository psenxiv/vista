using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimedRotationTests
{
    private static Quaternion At(float yaw, float pitch = 0f, float roll = 0f) =>
        CameraRotation.FromAngles(yaw, pitch, roll);

    private static float YawOf(Quaternion rotation) => CameraRotation.ToAngles(rotation).Yaw;

    private static void Facing(Vector3 expected, Quaternion rotation, float tolerance)
    {
        var forward = CameraRotation.Forward(rotation);
        Assert.Equal(expected.X, forward.X, tolerance);
        Assert.Equal(expected.Y, forward.Y, tolerance);
        Assert.Equal(expected.Z, forward.Z, tolerance);
    }

    [Fact]
    public void AHoldKeepsItsPointsRotationExactly()
    {
        // Point 1 holds from 2 s to 4 s: every time in the hold returns its rotation exactly.
        var rotations = new[] { At(0f), At(1f, 0.4f, 0.2f), At(2f) };
        var channel = new TimedRotation(rotations, [0f, 2f, 5f], [0f, 4f, 5f]);

        for (var t = 2.0; t <= 4.0; t += 0.125)
            Assert.Equal(rotations[1], channel.At(t));
    }

    [Fact]
    public void APureYawTurnsAtOneRateThroughAPointBetweenLegsOfDifferentTimes()
    {
        // Yaws 0, 1, 3 reached at 0, 2 and 5 s: legs of 1/2 and 2/3 rad/s, each weighted by the other leg's time, so point
        // 1 turns at (1/2·3 + 2/3·2) / 5 = 0.56667 rad/s, from either side, as a timed channel does.
        var channel = new TimedRotation([At(0f), At(1f), At(3f)], [0f, 2f, 5f], [0f, 2f, 5f]);
        const double h = 1e-3;

        Assert.Equal(1f, YawOf(channel.At(2.0)), 1e-5f);
        Assert.Equal(0.56667f, (float)((YawOf(channel.At(2.0)) - YawOf(channel.At(2.0 - h))) / h), 0.01f);
        Assert.Equal(0.56667f, (float)((YawOf(channel.At(2.0 + h)) - YawOf(channel.At(2.0))) / h), 0.01f);
    }

    [Fact]
    public void TheLastPointTurnsAtHalfItsLegsRate()
    {
        // Yaws 0, 1, 3 reached at 0, 2 and 3 s: the last leg turns 2 rad in 1 s, and an end point takes half its leg's
        // rate, 1 rad/s.
        var channel = new TimedRotation([At(0f), At(1f), At(3f)], [0f, 2f, 3f], [0f, 2f, 3f]);
        const double h = 1e-3;

        Assert.Equal(1f, (float)((YawOf(channel.At(3.0)) - YawOf(channel.At(3.0 - h))) / h), 0.02f);
    }

    [Fact]
    public void APointBeforeALegThatTakesNoTimeTurnsAtNoRate()
    {
        // Yaws 0, 1, 3 reached at 0, 2 and 2 s: the second leg takes no time, so point 1 turns at 0. The first leg starts
        // at half its own rate, (1/2)/2 = 0.25 rad/s, so halfway it's at Hermite(0, 1, 0.25·2, 0, ½) = 0.125·0.5 + 0.5·1
        // = 0.5625.
        var channel = new TimedRotation([At(0f), At(1f), At(3f)], [0f, 2f, 2f], [0f, 2f, 2f]);

        Assert.Equal(0.5625f, YawOf(channel.At(1.0)), 1e-5f);
    }

    [Fact]
    public void ARotationNeedsAnArrivalAndADepartureForEveryPoint()
    {
        Assert.Throws<ArgumentException>(() => new TimedRotation([], [], []));
        Assert.Throws<ArgumentException>(() => new TimedRotation([At(0f), At(1f)], [0f], [0f, 1f]));
        Assert.Throws<ArgumentException>(() => new TimedRotation([At(0f), At(1f)], [0f, 1f], [0f]));
    }

    [Fact]
    public void ABlendFromSixtyToOneHundredAndTwentyDegreesPitchPassesStraightUp()
    {
        // Pitch 120° is yaw 180°, pitch 60°, roll 180°. The shortest turn from pitch 60° is 60° more about the camera's
        // right, and a lone leg eases symmetrically, so halfway it's at pitch 90°: facing straight up.
        var channel = new TimedRotation([At(0f, 60f * Deg), At(MathF.PI, 60f * Deg, MathF.PI)], [0f, 2f], [0f, 2f]);

        Facing(Vector3.UnitY, channel.At(1.0), 1e-4f);
    }

    [Theory]
    [InlineData(1f, -1f)]
    [InlineData(-1f, 1f)]
    public void AHalfTurnOfYawTurnsTheWayTheYawDoes(float direction, float facingX)
    {
        // Yaw 0 to ±π is a half turn either way; it turns the way the angle does, so halfway it's at yaw ±π/2, facing
        // (∓1, 0, 0) by yaw = atan2(−x, −z).
        var channel = new TimedRotation([At(0f), At(direction * MathF.PI)], [0f, 2f], [0f, 2f]);

        Facing(new Vector3(facingX, 0f, 0f), channel.At(1.0), 1e-4f);
    }

    [Fact]
    public void APointFacingStraightUpBlendsToItsNeighbour()
    {
        // From straight up to level at the same yaw 0.7 is a quarter turn about the camera's right, so halfway it
        // faces yaw 0.7, pitch 45°.
        var channel = new TimedRotation([At(0.7f, MathF.PI / 2f), At(0.7f)], [0f, 2f], [0f, 2f]);
        var halfway = MathF.Sqrt(0.5f);

        Facing(new Vector3(-MathF.Sin(0.7f) * halfway, halfway, -MathF.Cos(0.7f) * halfway), channel.At(1.0), 1e-4f);
        for (var t = 0.0; t <= 2.0; t += 0.05)
            Assert.True(float.IsFinite(channel.At(t).W));
    }

    [Fact]
    public void BeforeTheFirstPointAndAfterTheLastItHoldsTheEnds()
    {
        var rotations = new[] { At(0f, 0.2f), At(1f) };
        var channel = new TimedRotation(rotations, [1f, 3f], [1f, 3f]);

        Assert.Equal(rotations[0], channel.At(0.0));
        Assert.Equal(rotations[1], channel.At(5.0));
    }
}
