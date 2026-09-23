using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class TrackEvaluatorTests
{
    private const float Deg = MathF.PI / 180f;

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

        // Leg 2 runs from 5 s to 10 s. The yaw channel is a Hermite through 0 and 90 degrees whose
        // phantom-duplicated endpoints make it pass through both exactly, and whose derivative
        // (pi / 4)(-6t^2 + 6t + 1) is positive across [0, 1], so the turn is strictly monotone between them.
        Assert.Equal(0f, YawAt(evaluator, 5.0) / Deg, 3);
        Assert.Equal(90f, YawAt(evaluator, 10.0) / Deg, 3);

        var previous = 0f;
        foreach (var time in new[] { 5.5, 6.0, 7.0, 7.5, 8.0, 9.0, 9.5 })
        {
            var yaw = YawAt(evaluator, time) / Deg;
            Assert.InRange(yaw, previous + 0.001f, 90f);
            previous = yaw;
        }
    }

    private static float YawAt(TrackEvaluator evaluator, double time)
    {
        var state = evaluator.Evaluate(time)!.Value;
        return TrackAim.FromDirection(state.LookAt - state.Position).Yaw;
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

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.WatchTarget)]
    public void ATargetAimsTheCameraAtIt(AimMode aim)
    {
        var track = TrackEditing.SetSpeed(Build(new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) }, aim), 5f);
        var target = new Vector3(10f, 5f, -30f);

        var state = new TrackEvaluator(track).Evaluate(2.0, target)!.Value;

        var look = Vector3.Normalize(state.LookAt - state.Position);
        var toTarget = Vector3.Normalize(target - state.Position);
        Assert.Equal(toTarget.X, look.X, 3);
        Assert.Equal(toTarget.Y, look.Y, 3);
        Assert.Equal(toTarget.Z, look.Z, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.WatchTarget)]
    public void WithNoTargetTheNewModesUseTheRecordedAim(AimMode aim)
    {
        var points = new[] { Point(0f, 0f, 0f, yaw: 90f * Deg), Point(10f, 0f, 0f, yaw: 90f * Deg) };

        var state = new TrackEvaluator(Build(points, aim)).Evaluate(1.0)!.Value;

        Assert.Equal(90f * Deg, TrackAim.FromDirection(state.LookAt - state.Position).Yaw, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.WatchTarget)]
    public void ASinglePointTrackTurnsToATarget(AimMode aim)
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f, fov: 1.2f, roll: 0.3f);

        var state = new TrackEvaluator(Build(new[] { point }, aim)).Evaluate(0.0, new Vector3(1f, 2f, -7f))!.Value;

        var (yaw, pitch) = TrackAim.FromDirection(state.LookAt - state.Position);
        Assert.Equal(point.Position, state.Position);
        Assert.Equal(0f, yaw, 4);
        Assert.Equal(0f, pitch, 4);
        Assert.Equal(1.2f, state.Fov);
        Assert.Equal(0.3f, state.Roll);
    }

    [Fact]
    public void ATargetOnTheCameraLeavesTheRecordedAim()
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f);

        var state = new TrackEvaluator(Build(new[] { point }, AimMode.LookAt)).Evaluate(0.0, new Vector3(1.05f, 2f, 3f))!.Value;

        Assert.Equal(FreeCamMotion.LookAtFrom(point.Position, 0.5f, 0.1f), state.LookAt);
    }

    // The direction the camera faces at time t.
    private static Vector3 Facing(TrackEvaluator evaluator, double t)
    {
        var frame = evaluator.Evaluate(t)!.Value;
        return Vector3.Normalize(frame.LookAt - frame.Position);
    }

    private static void Along(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-3f);
        Assert.Equal(expected.Y, actual.Y, 1e-3f);
        Assert.Equal(expected.Z, actual.Z, 1e-3f);
    }

    [Fact]
    public void LookingAheadOnAStraightPathFacesAlongIt()
    {
        var evaluator = new TrackEvaluator(Build([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent));

        Along(Vector3.UnitX, Facing(evaluator, 1.0));
        Along(Vector3.UnitX, Facing(evaluator, evaluator.Duration - 0.1));
    }

    [Fact]
    public void LookingAheadFacesWhereThePathIsThatMuchLater()
    {
        // The first leg takes 1 s and the look runs 1 s ahead, so at the start the camera faces point 2 exactly: (10, 0, 5).
        var track = TrackEditing.SetLookAhead(Build([Point(0f), Point(10f, z: 5f), Point(20f, z: -20f)], AimMode.PathTangent), 1f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLegDuration(track, 1, 1f));

        Along(Vector3.Normalize(new Vector3(10f, 0f, 5f)), Facing(evaluator, 0.0));
    }

    [Fact]
    public void LookingAheadNothingFacesStraightAlongThePath()
    {
        // Symmetric about the middle point, the path runs parallel to x there; 0.5 s on, it's already heading down towards the last.
        var track = Build([Point(-10f), Point(0f, z: 10f), Point(10f)], AimMode.PathTangent);
        var middle = new TrackEvaluator(track).PointSeconds(1);

        Along(Vector3.UnitX, Facing(new TrackEvaluator(TrackEditing.SetLookAhead(track, 0f)), middle));
        Assert.True(Facing(new TrackEvaluator(track), middle).Z < -0.1f);
    }

    [Fact]
    public void LookingAheadTurnsTowardsTheNextLegBeforeAHoldEnds()
    {
        // Point 2 holds 1 to 3 s, then a 0.3 s leg to point 3. At 2.8 s, still holding, 0.5 s on is point 3: (0, 0, 10) away.
        var track = Build([Point(0f), Point(10f), Point(10f, z: 10f)], AimMode.PathTangent);
        track = TrackEditing.SetHold(TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 1f), 2, 0.3f), 1, 2f);

        Along(Vector3.UnitZ, Facing(new TrackEvaluator(track), 2.8));
    }

    [Fact]
    public void RecordedAimTurnsAtOneRateThroughAPointBetweenLegsOfDifferentTimes()
    {
        // Legs of 10 and 5 yalms at 5 a second take 2 s and 1 s; yaw 0, 1, 3 gives (1/2·1 + 2/1·2) / 3 = 1.5 rad/s at the middle.
        var evaluator = new TrackEvaluator(Build([Point(0f, yaw: 0f), Point(10f, yaw: 1f), Point(15f, yaw: 3f)]) with { Speed = 5f });
        float Yaw(double time) => TrackAim.FromDirection(Facing(evaluator, time)).Yaw;
        const double h = 1e-3;

        Assert.Equal(1.5f, (float)((Yaw(2.0) - Yaw(2.0 - h)) / h), 0.02f);
        Assert.Equal(1.5f, (float)((Yaw(2.0 + h) - Yaw(2.0)) / h), 0.02f);
    }

    [Fact]
    public void RecordedAimHoldsStillThroughAHold()
    {
        // Point 2 holds for 2 s: through the hold the camera keeps point 2's yaw, 1.
        var track = TrackEditing.SetHold(Build([Point(0f, yaw: 0f), Point(10f, yaw: 1f), Point(20f, yaw: 2f)]), 1, 2f);
        var evaluator = new TrackEvaluator(track);
        var arrive = evaluator.PointSeconds(1);

        Assert.Equal(1f, TrackAim.FromDirection(Facing(evaluator, arrive + 1.9)).Yaw, 1e-4f);
    }
}
