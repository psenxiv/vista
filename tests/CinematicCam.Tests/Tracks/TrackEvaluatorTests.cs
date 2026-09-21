using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class TrackEvaluatorTests
{
    private const float Deg = MathF.PI / 180f;

    private static ControlPoint Point(float x, float y, float z, float yaw = 0f, float pitch = 0f, float fov = 1f)
        => new(new Vector3(x, y, z), yaw, pitch, fov);

    private static TimingKey Key(float time, float position, TangentMode mode = TangentMode.Auto)
        => new(time, position, mode, 0f, 0f);

    [Fact]
    public void EvaluateReturnsNullForATrackWithNoPoints()
    {
        var track = new Track(Array.Empty<ControlPoint>(), Array.Empty<TimingKey>(), AimMode.AimKeys, PlaybackMode.Once);
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(0.0, evaluator.Duration);
        Assert.Null(evaluator.Evaluate(0.0));
        Assert.Null(evaluator.Evaluate(5.0));
    }

    [Fact]
    public void EvaluateOfASinglePointTrackSitsAtThatPointRegardlessOfTime()
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f, fov: 1.2f);
        var track = new Track(new[] { point }, Array.Empty<TimingKey>(), AimMode.AimKeys, PlaybackMode.Once);
        var evaluator = new TrackEvaluator(track);
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
    public void NoTimingKeysWithMultiplePointsSitsAtTheFirstPoint()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) };
        var track = new Track(points, Array.Empty<TimingKey>(), AimMode.AimKeys, PlaybackMode.Once);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(3.0);
        Assert.NotNull(state);
        Assert.Equal(points[0].Position, state!.Value.Position);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(1f, 1f) }, AimMode.AimKeys, PlaybackMode.Once);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(4f, 4f) }, AimMode.PathTangent, PlaybackMode.Once);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(4f, 4f) }, AimMode.AimKeys, PlaybackMode.Once);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(5f, 1f), Key(10f, 2f) }, AimMode.AimKeys, PlaybackMode.Once);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(10f, 2f) }, AimMode.AimKeys, PlaybackMode.Once);
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
        var track = new Track(points, new[] { Key(0f, 0f), Key(5f, 1f), Key(10f, 2f) }, AimMode.AimKeys, PlaybackMode.Once);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(7.5)!.Value;
        var yaw = TrackAim.FromDirection(state.LookAt - state.Position).Yaw;

        Assert.InRange(yaw, 20f * Deg, 70f * Deg);
    }
}
