using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class TrackEvaluatorTests
{
    private const float Deg = MathF.PI / 180f;

    private static ControlPoint Point(float x, float y, float z, float yaw = 0f, float pitch = 0f, float fov = 1f, float roll = 0f)
        => new(new Vector3(x, y, z), yaw, pitch, fov, roll);

    private static Track Build(IEnumerable<ControlPoint> points, AimMode aim = AimMode.AimKeys)
    {
        var track = TrackEditing.Empty(aim) with { Speed = 2f };
        foreach (var point in points) track = TrackEditing.Append(track, point);
        return track;
    }

    [Fact]
    public void EvaluateReturnsNullForATrackWithNoPoints()
    {
        var evaluator = new TrackEvaluator(TrackEditing.Empty());

        Assert.Equal(0.0, evaluator.Duration);
        Assert.Null(evaluator.Evaluate(0.0));
        Assert.Null(evaluator.Evaluate(5.0));
    }

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    public void EvaluateOfASinglePointTrackSitsAtThatPointRegardlessOfTime(AimMode aim)
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f, fov: 1.2f);
        var evaluator = new TrackEvaluator(Build(new[] { point }, aim));
        var expectedLookAt = FreeCamMotion.LookAtFrom(point.Position, point.Yaw, point.Pitch);

        foreach (var time in new[] { -1.0, 0.0, 5.0 })
        {
            var state = evaluator.Evaluate(time);
            Assert.NotNull(state);
            Assert.Equal(point.Position, state!.Value.Position);
            Assert.Equal(expectedLookAt, state.Value.LookAt);
            Assert.Equal(point.Fov, state.Value.Fov);
        }
    }

    [Fact]
    public void AimKeysSplinesYawTheShortWayAcrossPlusMinus180()
    {
        // Point 0 at 170deg, point 1 at -170deg: the short way is through 180deg, not 0.
        var points = new[]
        {
            Point(0f, 0f, 0f, yaw: 170f * Deg),
            Point(10f, 0f, 0f, yaw: -170f * Deg),
        };
        var track = TrackEditing.SetLegDuration(Build(points), 1, 1f);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(0.5);
        Assert.NotNull(state);

        // Compared as a look-at vector rather than an angle recovered via atan2: at
        // exactly 180deg the recovered angle can land on either side of the +-pi seam,
        // which the vector comparison sidesteps.
        var expectedLookAt = FreeCamMotion.LookAtFrom(state!.Value.Position, 180f * Deg, 0f);
        Assert.Equal(expectedLookAt.X, state.Value.LookAt.X, 3);
        Assert.Equal(expectedLookAt.Y, state.Value.LookAt.Y, 3);
        Assert.Equal(expectedLookAt.Z, state.Value.LookAt.Z, 3);
    }

    [Fact]
    public void PathTangentAimFollowsTheDirectionOfTravelOnAStraightLeg()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) };
        var track = TrackEditing.SetSpeed(Build(points, AimMode.PathTangent), 5f);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(2.0);
        Assert.NotNull(state);

        var toLookAt = Vector3.Normalize(state!.Value.LookAt - state.Value.Position);
        var expected = TrackAim.FromDirection(new Vector3(1f, 0f, 0f));
        var actual = TrackAim.FromDirection(toLookAt);

        Assert.Equal(expected.Yaw, actual.Yaw, 3);
        Assert.Equal(expected.Pitch, actual.Pitch, 3);
    }

    [Fact]
    public void FovIsClampedToTheAuthoredMinAndMax()
    {
        // Fov spikes at the middle point; uniform Catmull-Rom overshoots below the
        // authored minimum on the far side, which must be clamped back to it.
        var points = new[]
        {
            Point(0f, 0f, 0f, fov: 1f),
            Point(10f, 0f, 0f, fov: 1f),
            Point(20f, 0f, 0f, fov: 3f),
            Point(30f, 0f, 0f, fov: 1f),
            Point(40f, 0f, 0f, fov: 1f),
        };
        var track = TrackEditing.SetSpeed(Build(points), 10f);
        var evaluator = new TrackEvaluator(track);

        // Position 3.335 lands in segment 3 at fraction 0.335, where the raw spline
        // value dips to roughly 0.85 - below the authored minimum of 1.
        var state = evaluator.Evaluate(3.335);
        Assert.NotNull(state);
        Assert.Equal(1f, state!.Value.Fov, 2);
    }

    [Fact]
    public void WorldSpeedIsContinuousThroughAKeyBetweenUnequalSegments()
    {
        // Collinear points, so arc length is chord length: a 10 m leg then a 30 m leg, 5 s each.
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(40f, 0f, 0f) };
        var track = TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(Build(points), 1, 5f), 2, 5f);
        var evaluator = new TrackEvaluator(track);

        const double eps = 1e-2;
        var at = evaluator.Evaluate(5.0)!.Value.Position;
        var before = Vector3.Distance(evaluator.Evaluate(5.0 - eps)!.Value.Position, at) / eps;
        var after = Vector3.Distance(at, evaluator.Evaluate(5.0 + eps)!.Value.Position) / eps;

        Assert.True(Math.Abs(before - after) < 0.05 * after, $"speed steps at the key: {before} m/s before, {after} m/s after");
        Assert.Equal(points[1].Position.X, at.X, 3);
    }

    [Fact]
    public void AStraightTimingCurveGivesConstantWorldSpeedAcrossUnequalSegments()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(40f, 0f, 0f) };
        var track = TrackEditing.SetSpeed(Build(points), 4f);
        var evaluator = new TrackEvaluator(track);

        for (var i = 0; i <= 10; i++)
            Assert.Equal(4f * i, evaluator.Evaluate(i)!.Value.Position.X, 2);
    }

    [Fact]
    public void ALegBetweenCoincidentPointsStillTurnsOverItsDuration()
    {
        var points = new[]
        {
            Point(0f, 0f, 0f),
            Point(10f, 0f, 0f),
            Point(10f, 0f, 0f, yaw: 90f * Deg),
        };
        var track = TrackEditing.SetLegDuration(Build(points), 2, 5f);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(7.5)!.Value;
        var yaw = TrackAim.FromDirection(state.LookAt - state.Position).Yaw;

        Assert.InRange(yaw, 20f * Deg, 70f * Deg);
    }

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    public void RollBlendsBetweenPointsInEitherAimMode(AimMode aim)
    {
        var track = TrackEditing.SetLegDuration(Build(new[] { Point(0f, 0f, 0f, roll: 0f), Point(10f, 0f, 0f, roll: 90f * Deg) }, aim), 1, 10f);
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(0f, evaluator.Evaluate(0.0)!.Value.Roll, 4);
        Assert.Equal(45f * Deg, evaluator.Evaluate(5.0)!.Value.Roll, 2);
        Assert.Equal(90f * Deg, evaluator.Evaluate(10.0)!.Value.Roll, 4);
    }

    [Fact]
    public void AChainOfSmallRollsBuildsAFullBarrelRoll()
    {
        var rolls = new[] { 0f, 90f, 180f, -90f, 0f };
        var points = rolls.Select((r, i) => Point(i * 10f, 0f, 0f, roll: r * Deg)).ToArray();
        var evaluator = new TrackEvaluator(Build(points));

        var previous = float.NegativeInfinity;
        for (var t = 0.0; t <= 20.0; t += 0.25)
        {
            var roll = evaluator.Evaluate(t)!.Value.Roll;
            Assert.True(roll >= previous - 1e-4f, $"roll went backwards at t={t}: {previous} -> {roll}");
            previous = roll;
        }

        Assert.Equal(2f * MathF.PI, evaluator.Evaluate(20.0)!.Value.Roll, 3);
    }

    [Fact]
    public void ASinglePointTrackKeepsItsRoll()
    {
        var track = Build(new[] { Point(1f, 2f, 3f, roll: 0.3f) });

        Assert.Equal(0.3f, new TrackEvaluator(track).Evaluate(0.0)!.Value.Roll);
    }

    // Points on a line at x = 0, 10, 20; Linear keys at 0, 5, 10 s.
    private static TrackEvaluator StraightLinear()
    {
        var points = new[] { 0f, 10f, 20f }.Select(x => new ControlPoint(new Vector3(x, 0f, 0f), 0f, 0f, 1f)).ToArray();
        var track = Build(points);
        for (var key = 0; key < 3; key++) track = TimingEditing.SetKeyMode(track, key, TangentMode.Linear);
        return new TrackEvaluator(track);
    }

    [Fact]
    public void DistanceQueriesFollowThePath()
    {
        var evaluator = StraightLinear();
        Assert.Equal(20f, evaluator.TotalDistance, 1);
        Assert.Equal(5f, evaluator.DistanceAt(2.5), 1);
        Assert.Equal(2f, evaluator.SlopeAt(2.5), 1);
        Assert.Equal(15f, evaluator.DistanceOf(1.5f), 1);
        Assert.Equal(1.5f, evaluator.PositionOf(15f), 2);
    }

    [Fact]
    public void SideSlopesAreInDistancePerSecond()
    {
        var evaluator = StraightLinear();
        Assert.Equal(2f, evaluator.SideSlope(1, KeySide.Out), 1);
        Assert.Equal(2f, evaluator.SideSlope(1, KeySide.In), 1);
    }

    [Fact]
    public void StoredSlopesAreRatiosToTheSpansSecant()
    {
        var evaluator = StraightLinear();
        Assert.Equal(1f, evaluator.ToStoredSlope(1, KeySide.Out, 2f), 3);
        Assert.Equal(1f, evaluator.FromStoredSlope(1, KeySide.In, 0.5f), 3);
    }

    [Fact]
    public void AStoredSlopeWithNoSpanIsZero()
    {
        var evaluator = StraightLinear();
        Assert.Equal(0f, evaluator.ToStoredSlope(0, KeySide.In, 2f));
        Assert.Equal(0f, evaluator.FromStoredSlope(2, KeySide.Out, 0.5f));
    }

    [Fact]
    public void AOnePointTrackHasNoDistance()
    {
        var point = new ControlPoint(Vector3.Zero, 0f, 0f, 1f);
        var evaluator = new TrackEvaluator(Build(new[] { point }));
        Assert.Equal(0f, evaluator.TotalDistance);
        Assert.Equal(0f, evaluator.DistanceAt(1.0));
        Assert.Equal(0f, evaluator.PositionOf(3f));
    }
}
