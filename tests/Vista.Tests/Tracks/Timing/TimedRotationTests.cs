using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Timing.TimingFixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimedRotationTests
{
    private static Quaternion At(float yaw, float pitch = 0f, float roll = 0f) =>
        CameraRotation.FromAngles(yaw, pitch, roll);

    private static float YawOf(Quaternion rotation) => CameraRotation.ToAngles(rotation).Yaw;

    /// <summary>How far either side of a middle point the world turn rate is sampled, in seconds: close enough that a leg's own curvature barely moves it, far enough that float round-off in <see cref="WorldTurnRate"/>'s 1e-4 s window stays well under the agreement tolerance.</summary>
    private const double RateOffset = 1e-3;

    /// <summary>The central-difference window <see cref="WorldTurnRate"/> measures each side's rate over.</summary>
    private const double RateWindow = 1e-4;

    /// <summary>How far apart, in rad/s, the world turn rate either side of a middle point may be and still count as one rate: comfortably above the finite-difference noise a correct implementation leaves (under 0.031 rad/s over 500,000 random three-point legs of 1 to 5 s, checked while writing this test), and far below the tenths of a rad/s a one-sided rate (the old behaviour) leaves.</summary>
    private const float RateAgreement = 0.05f;

    [Fact]
    public void ARecordedAimTrackTurnsAtOneRateThroughAMiddlePoint()
    {
        // The user's shot: east 45° up and upright, straight up with the picture's top to the north (-z), west 45° up
        // and upright, 2 s apart. The middle point's turn so far, going in, is the whole first leg's; its rotation-vector
        // rate must be mapped through the inverse left Jacobian of the exponential map there for the camera's actual
        // world turn rate arriving to equal the rate it leaves with, since only one rate can pass through a point.
        var east = CameraRotation.FromBasis(
            Vector3.Normalize(new Vector3(1f, 1f, 0f)),
            Vector3.Normalize(new Vector3(-1f, 1f, 0f))
        );
        var up = CameraRotation.FromBasis(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));
        var west = CameraRotation.FromBasis(
            Vector3.Normalize(new Vector3(-1f, 1f, 0f)),
            Vector3.Normalize(new Vector3(1f, 1f, 0f))
        );
        var channel = new TimedRotation([east, up, west], [0f, 2f, 4f], [0f, 2f, 4f]);

        var before = WorldTurnRate(channel.At, 2.0 - RateOffset, RateWindow);
        var after = WorldTurnRate(channel.At, 2.0 + RateOffset, RateWindow);

        Assert.True(
            Vectors.AngleBetween(before, after) <= 0.5f * Deg,
            $"Axis differs by {Vectors.AngleBetween(before, after) / Deg:0.###}°"
        );
        var (beforeSpeed, afterSpeed) = (before.Length(), after.Length());
        Assert.True(
            MathF.Abs(beforeSpeed - afterSpeed) <= 0.01f * ((beforeSpeed + afterSpeed) / 2f),
            $"Speed differs: {beforeSpeed:0.###} vs {afterSpeed:0.###} rad/s"
        );
    }

    /// <summary>A recorded-aim track through 3 to 6 points (<see cref="AnyPoint"/>), each leg pinned to a duration of 1 to 5 s, no holds, built as the editor builds it.</summary>
    private static readonly Gen<Track> AnyRecordedAimTrack =
        from points in AnyPoint.Array[3, 6]
        from legSeconds in Gen.Float[1f, 5f].Array[points.Length]
        select RecordedAimTrack(points, legSeconds);

    private static Track RecordedAimTrack(ControlPoint[] points, float[] legSeconds)
    {
        var track = TrackEditing.Empty(AimMode.AimKeys);
        foreach (var point in points)
            track = TrackEditing.Append(track, point);
        for (var leg = 1; leg < points.Length; leg++)
            track = TrackEditing.SetLegDuration(track, leg, legSeconds[leg]);
        return track;
    }

    [Fact]
    [Trait("Category", "Property")]
    public void ARecordedAimTrackTurnsAtOneRateThroughEveryMiddlePoint()
    {
        AnyRecordedAimTrack.Sample(
            track =>
            {
                var evaluator = new TrackEvaluator(track);
                var rotation = RecordedAimRotation(evaluator);
                for (var point = 1; point < track.Points.Count - 1; point++)
                {
                    var t = evaluator.PointSeconds(point);
                    var before = WorldTurnRate(rotation, t - RateOffset, RateWindow);
                    var after = WorldTurnRate(rotation, t + RateOffset, RateWindow);
                    var diff = (before - after).Length();
                    if (!(diff <= RateAgreement))
                        Assert.Fail(
                            $"Point {point}: turn rate differs by {diff:0.#####} rad/s either side of {t:0.###} s"
                        );
                }
            },
            iter: 2000,
            print: Kept<Track>(PrintTrack)
        );
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
        var (left, right) = Slopes(t => YawOf(channel.At(t)), 2.0, 1e-3);

        Assert.Equal(1f, YawOf(channel.At(2.0)), 1e-5f);
        Assert.Equal(0.56667f, left, 0.01f);
        Assert.Equal(0.56667f, right, 0.01f);
    }

    [Fact]
    public void TheLastPointTurnsAtHalfItsLegsRate()
    {
        // Yaws 0, 1, 3 reached at 0, 2 and 3 s: the last leg turns 2 rad in 1 s, and an end point takes half its leg's
        // rate, 1 rad/s.
        var channel = new TimedRotation([At(0f), At(1f), At(3f)], [0f, 2f, 3f], [0f, 2f, 3f]);
        Assert.Equal(1f, Slopes(t => YawOf(channel.At(t)), 3.0, 1e-3).Left, 0.02f);
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

        Near(Vector3.UnitY, CameraRotation.Forward(channel.At(1.0)), 1e-4f);
    }

    [Theory]
    [InlineData(1f, -1f)]
    [InlineData(-1f, 1f)]
    public void AHalfTurnOfYawTurnsTheWayTheYawDoes(float direction, float facingX)
    {
        // Yaw 0 to ±π is a half turn either way; it turns the way the angle does, so halfway it's at yaw ±π/2, facing
        // (∓1, 0, 0) by yaw = atan2(−x, −z).
        var channel = new TimedRotation([At(0f), At(direction * MathF.PI)], [0f, 2f], [0f, 2f]);

        Near(new Vector3(facingX, 0f, 0f), CameraRotation.Forward(channel.At(1.0)), 1e-4f);
    }

    [Fact]
    public void APointFacingStraightUpBlendsToItsNeighbour()
    {
        // From straight up to level at the same yaw 0.7 is a quarter turn about the camera's right, so halfway it
        // faces yaw 0.7, pitch 45°.
        var channel = new TimedRotation([At(0.7f, MathF.PI / 2f), At(0.7f)], [0f, 2f], [0f, 2f]);
        var halfway = MathF.Sqrt(0.5f);

        Near(
            new Vector3(-MathF.Sin(0.7f) * halfway, halfway, -MathF.Cos(0.7f) * halfway),
            CameraRotation.Forward(channel.At(1.0)),
            1e-4f
        );
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
