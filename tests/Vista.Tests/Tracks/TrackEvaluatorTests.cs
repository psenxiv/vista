using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class TrackEvaluatorTests
{
    private const float Deg = MathF.PI / 180f;

    private static Track Build(IEnumerable<ControlPoint> points, AimMode aim = AimMode.AimKeys)
    {
        var track = TrackEditing.Empty(aim) with { Speed = 2f };
        foreach (var point in points)
            track = TrackEditing.Append(track, point);
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
        var points = new[] { Point(0f, 0f, 0f, yaw: 170f * Deg), Point(10f, 0f, 0f, yaw: -170f * Deg) };
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

        Assert.True(
            Math.Abs(before - after) < 0.05 * after,
            $"speed steps at the key: {before} m/s before, {after} m/s after"
        );
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
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(10f, 0f, 0f, yaw: 90f * Deg) };
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
        var track = TrackEditing.SetLegDuration(
            Build(new[] { Point(0f, 0f, 0f, roll: 0f), Point(10f, 0f, 0f, roll: 90f * Deg) }, aim),
            1,
            10f
        );
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
        for (var key = 0; key < 3; key++)
            track = TimingEditing.SetKeyMode(track, key, TangentMode.Linear);
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
        var track = TrackEditing.SetSpeed(
            Build(new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) }, aim),
            5f
        );
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

        var state = new TrackEvaluator(Build(new[] { point }, AimMode.LookAt))
            .Evaluate(0.0, new Vector3(1.05f, 2f, 3f))!
            .Value;

        Assert.Equal(FreeCamMotion.LookAtFrom(point.Position, 0.5f, 0.1f), state.LookAt);
    }

    // The direction the camera faces at time t, aimed at target when given.
    private static Vector3 Facing(TrackEvaluator evaluator, double t, Vector3? target = null)
    {
        var frame = evaluator.Evaluate(t, target)!.Value;
        return Vector3.Normalize(frame.LookAt - frame.Position);
    }

    private static void Along(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    [Fact]
    public void LookingAheadOnAStraightPathFacesAlongIt()
    {
        var evaluator = new TrackEvaluator(Build([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent));

        Along(Vector3.UnitX, Facing(evaluator, 1.0), 1e-3f);
        Along(Vector3.UnitX, Facing(evaluator, evaluator.Duration - 0.1), 1e-3f);
        Along(Vector3.UnitX, Facing(evaluator, evaluator.Duration), 1e-3f);
    }

    [Fact]
    public void LookingAheadFacesWhereThePathIsThatMuchLater()
    {
        // The first leg takes 1 s and the look runs 1 s ahead, so at the start the camera faces point 2 exactly: (10, 0, 5).
        var track = TrackEditing.SetLookAhead(
            Build([Point(0f), Point(10f, z: 5f), Point(20f, z: -20f)], AimMode.PathTangent),
            1f
        );
        var evaluator = new TrackEvaluator(TrackEditing.SetLegDuration(track, 1, 1f));

        Along(Vector3.Normalize(new Vector3(10f, 0f, 5f)), Facing(evaluator, 0.0), 1e-3f);
    }

    [Fact]
    public void LookingAheadNothingFacesStraightAlongThePath()
    {
        // Symmetric about the middle point, the path runs parallel to x there. With the last leg taking 0.5 s,
        // the default look 0.5 s ahead instead faces the last point: (10, 0, -10) away.
        var track = TrackEditing.SetLegDuration(
            Build([Point(-10f), Point(0f, z: 10f), Point(10f)], AimMode.PathTangent),
            2,
            0.5f
        );
        var middle = new TrackEvaluator(track).PointSeconds(1);

        Along(Vector3.UnitX, Facing(new TrackEvaluator(TrackEditing.SetLookAhead(track, 0f)), middle), 1e-3f);
        Along(Vector3.Normalize(new Vector3(10f, 0f, -10f)), Facing(new TrackEvaluator(track), middle), 1e-3f);
    }

    [Fact]
    public void LookingAheadSnapsRoundWhereThePathRunsStraightBackAlongItself()
    {
        // Out to x = 10 and back to 5 at 2 yalms a second, the path turns round at 10 at 5 s: the camera is at 2t and
        // the spot 1 s ahead at 20 − 2(t + 1). They pass at 4.5 s, so the facing flips from +x to −x there at once.
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(Build([Point(0f), Point(10f), Point(5f)], AimMode.PathTangent), 1f)
        );

        Along(Vector3.UnitX, Facing(evaluator, 4.49), 1e-3f);
        Along(-Vector3.UnitX, Facing(evaluator, 4.51), 1e-3f);
    }

    [Theory]
    [InlineData(1e-3)]
    [InlineData(4e-3)]
    [InlineData(1.6e-2)]
    public void LookingAheadStaysSteadyJustBeforeTheEndFarFromTheOrigin(double early)
    {
        // Far from the origin, a look-ahead chord this short is mostly float rounding, so it falls back to the path's own
        // direction and matches the look at the very end to within a degree (cos 1° ≈ 0.99985).
        var track = Build(
            [Point(612f, 42f, -488f), Point(620f, 42f, -480f), Point(631f, 43f, -489f)],
            AimMode.PathTangent
        );
        var evaluator = new TrackEvaluator(track);

        Assert.InRange(
            Vector3.Dot(Facing(evaluator, evaluator.Duration - early), Facing(evaluator, evaluator.Duration)),
            0.99985f,
            1.0001f
        );
    }

    [Fact]
    public void LookingAheadTurnsTowardsTheNextLegBeforeAHoldEnds()
    {
        // Point 2 holds 1 to 3 s, then a 0.3 s leg to point 3. At 2.8 s, still holding, 0.5 s on is point 3: (0, 0, 10) away.
        var track = Build([Point(0f), Point(10f), Point(10f, z: 10f)], AimMode.PathTangent);
        track = TrackEditing.SetHold(
            TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 1f), 2, 0.3f),
            1,
            2f
        );

        Along(Vector3.UnitZ, Facing(new TrackEvaluator(track), 2.8), 1e-3f);
    }

    [Fact]
    public void LookingAheadTurnsSmoothlyIntoAndOutOfAHold()
    {
        // Point 2 holds for 2 s. Leaving it, the turn peaks near 20 deg/s, 0.02° a millisecond; snapping between the path's
        // own direction and the spot ahead at 0.1 yalm stepped 1.7° at once. 0.1° allows the turn five times over.
        var track = TrackEditing.SetHold(
            Build([Point(-10f), Point(0f, z: 10f), Point(10f)], AimMode.PathTangent),
            1,
            2f
        );
        var evaluator = new TrackEvaluator(track);
        var arrive = evaluator.PointSeconds(1);

        for (var t = arrive - 0.5; t < arrive + 2.5; t += 1e-3)
        {
            // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
            Assert.InRange(Vector3.Distance(Facing(evaluator, t), Facing(evaluator, t + 1e-3)), 0f, 0.1f * Deg);
        }
    }

    [Fact]
    public void LookingAheadAcrossAHairpinFacesTheSpotAhead()
    {
        // The path turns back 0.2 yalm from itself. At 1.5 s the spot 2 s on is 4 yalms further along the path, so the
        // camera faces straight at it, where the camera will be at 3.5 s, though it's under a yalm away across the gap.
        var track = TrackEditing.SetLookAhead(
            Build([Point(0f), Point(5f), Point(5f, z: 0.2f), Point(0f, z: 0.2f)], AimMode.PathTangent),
            2f
        );
        var evaluator = new TrackEvaluator(track);

        AimsAt(evaluator.Evaluate(3.5)!.Value.Position, evaluator.Evaluate(1.5)!.Value, 4);
    }

    [Fact]
    public void LookingAheadSettlesIntoTheDemosLastPointWithoutAStep()
    {
        // East Hawker eases into its last point turning about 1.5 deg/s, 0.025° a frame at 60 fps. Snapping to the exact
        // tangent 0.1 yalm out stepped 0.12° in one frame; 0.05° allows the steady turn twice over and catches the step.
        var track = DemoScene().Tracks.Single(t => t.Name == "East Hawker fly through");
        var evaluator = new TrackEvaluator(track);
        var end = evaluator.PointSeconds(track.Points.Count - 1);

        for (var t = end - 1.5; t < end + 0.5; t += 1.0 / 60.0)
        {
            // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
            Assert.InRange(
                Vector3.Distance(Facing(evaluator, t), Facing(evaluator, t + (1.0 / 60.0))),
                0f,
                0.05f * Deg
            );
        }
    }

    [Fact]
    public void RecordedAimTurnsAtOneRateThroughAPointBetweenLegsOfDifferentTimes()
    {
        // Legs of 10 and 5 yalms at 5 a second take 2 s and 1 s; yaw 0, 1, 3 gives (1/2·1 + 2/1·2) / 3 = 1.5 rad/s at the middle.
        var evaluator = new TrackEvaluator(
            Build([Point(0f, yaw: 0f), Point(10f, yaw: 1f), Point(15f, yaw: 3f)]) with
            {
                Speed = 5f,
            }
        );
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

    // The largest turn of the facing from one millisecond to the next across the whole shot, in radians.
    private static float LargestMillisecondTurn(TrackEvaluator evaluator)
    {
        var largest = 0f;
        for (var t = 0.0; t < evaluator.Duration; t += 1e-3)
            largest = MathF.Max(largest, Vector3.Distance(Facing(evaluator, t), Facing(evaluator, t + 1e-3)));
        return largest;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void DirectionOfTravelTurnsSmoothlyAsThePathDriftsAcrossTheVertical(float lookAhead)
    {
        // The path climbs 10 yalms, bowing 0.2 to +x and back, so its direction passes within 1° of vertical from +x to -x.
        // Yaw read from that tiny sideways part flipped half a turn at once: a step of 2 sin 1° ≈ 2° in one millisecond.
        // Turned evenly over the stretch of about half a second, the half turn round the vertical moves the facing
        // π sin 1° ≈ 3.1°, near 0.006° a millisecond, and the bow turns it no faster than 0.01°. 0.05° allows five times that.
        var track = TrackEditing.SetLookAhead(
            Build([Point(0f), Point(0.2f, 5f), Point(0f, 10f)], AimMode.PathTangent),
            lookAhead
        );

        // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
        Assert.InRange(LargestMillisecondTurn(new TrackEvaluator(track)), 0f, 0.05f * Deg);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void DirectionOfTravelTurnsSmoothlyThroughACraneShot(float lookAhead)
    {
        // Along -z, straight up, then along +x. FromDirection's yaw is atan2(-x, -z): 0 heading -z and -π/2 heading +x.
        // The first leg lies in the plane x = 0 and the last in z = -10, so the headings are exact there.
        var track = TrackEditing.SetLookAhead(
            Build(
                [Point(0f), Point(0f, 0f, -10f), Point(0f, 10f, -10f), Point(0f, 20f, -10f), Point(10f, 20f, -10f)],
                AimMode.PathTangent
            ),
            lookAhead
        );
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(0f, TrackAim.FromDirection(Facing(evaluator, 2.0)).Yaw, 1e-4f);
        Assert.Equal(-MathF.PI / 2f, TrackAim.FromDirection(Facing(evaluator, evaluator.Duration - 2.0)).Yaw, 1e-4f);

        // The climb bows either side of the vertical, and yaw read from the tiny sideways part stepped up to half a turn at
        // once there, 2 sin 1° ≈ 2°. Turned evenly, at most a half turn round the vertical, π sin 1° ≈ 3.1°, spread over a
        // stretch of at least 0.09 s is under 0.04° a millisecond. The sharpest turns are the bends into and out of the
        // climb, just under 0.1° a millisecond with look ahead 0; 0.2° allows them twice over.
        Assert.InRange(LargestMillisecondTurn(evaluator), 0f, 0.2f * Deg);
    }

    // Facing at pitch 89° with yaw π/2: (-sin(π/2) cos 89°, sin 89°, -cos(π/2) cos 89°).
    private static readonly Vector3 UpHeadingMinusX = new(-0.017452406f, 0.99984770f, 0f);

    [Fact]
    public void DirectionOfTravelThatStartsVerticalHoldsTheYawItLeavesWith()
    {
        // Up through (0, 10, 0) and (0, 20, 0), then level to (10, 20, 0). Segment 1's last blend extrapolates the leg to
        // point 4 backwards, to x ≤ 0, so the path bows to -x before bending to +x: it leaves the vertical heading -x.
        var evaluator = new TrackEvaluator(
            Build([Point(0f), Point(0f, 10f), Point(0f, 20f), Point(10f, 20f)], AimMode.PathTangent)
        );

        Along(UpHeadingMinusX, Facing(evaluator, 0.0), 1e-4f);
        Along(UpHeadingMinusX, Facing(evaluator, 2.0), 1e-4f);
    }

    [Fact]
    public void DirectionOfTravelThatEndsVerticalHoldsTheYawItArrivesWith()
    {
        // Level from (-10, 0, 0) to the origin, then up through (0, 10, 0) to (0, 20, 0). Segment 1's first blend
        // extrapolates the leg from point 1 onwards, to x ≥ 0, so the path bows to +x and comes back: it reaches the
        // vertical heading -x.
        var evaluator = new TrackEvaluator(
            Build([Point(-10f), Point(0f), Point(0f, 10f), Point(0f, 20f)], AimMode.PathTangent)
        );

        Along(UpHeadingMinusX, Facing(evaluator, evaluator.Duration - 2.0), 1e-4f);
        Along(UpHeadingMinusX, Facing(evaluator, evaluator.Duration), 1e-4f);
    }

    [Fact]
    public void DirectionOfTravelThatIsVerticalThroughoutKeepsTheFirstPointsYaw()
    {
        // Straight up, pitch clamped to 89° at yaw 0.3: (-sin 0.3 cos 89°, sin 89°, -cos 0.3 cos 89°).
        var evaluator = new TrackEvaluator(Build([Point(0f, yaw: 0.3f), Point(0f, 10f)], AimMode.PathTangent));

        Along(new Vector3(-0.0051575388f, 0.99984770f, -0.016672921f), Facing(evaluator, 1.0), 1e-4f);
    }

    [Fact]
    public void DirectionOfTravelTurnsSmoothlyThroughAStretchShorterThanTheScanStep()
    {
        // A sharp turn in the plane z = 0: looking ahead, the aim sweeps down through the vertical at about 0.57° a
        // millisecond, so it's within 1° of vertical for about 3.5 ms, less than the 10 ms scan step. Missed, the yaw
        // flips half a turn at once: a step of 2 sin 1° ≈ 2°. Turned evenly, the half turn moves the facing π sin 1° ≈ 3.1°
        // over those 3.5 ms, under 0.001° a microsecond; 0.01° allows ten times that.
        var track = TrackEditing.SetSpeed(
            Build([Point(18.772842f, 1.7956989f), Point(0f), Point(23f, -3.1298702f)], AimMode.PathTangent),
            10.942029f
        );
        track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(track, 1, 9.457632f), 2, 2.3922946f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(TrackEditing.SetHold(track, 2, 1.1724138f), 1f));

        var largest = 0f;
        for (var t = 1.58; t < 1.61; t += 1e-6)
            largest = MathF.Max(largest, Vector3.Distance(Facing(evaluator, t), Facing(evaluator, t + 1e-6)));
        Assert.InRange(largest, 0f, 0.01f * Deg);
    }

    [Fact]
    public void DirectionOfTravelHoldsItsYawThroughAHoldInsideAVerticalStretch()
    {
        // Point 2 is reached straight down from point 1 and holds, so the aim is vertical through the hold. The camera and
        // the spot 2 s ahead both stay at point 2 until 2 s before the hold ends, so no distance is travelled and the yaw,
        // turned with distance, stays where it arrived.
        var track = TrackEditing.SetSpeed(
            Build([Point(0f, 2f), Point(0f, 1f), Point(26f, 3.4449844f, 12.046512f)], AimMode.PathTangent),
            18f
        );
        track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(track, 1, 0.14192629f), 2, 7.279038f);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 0, 2f), 1, 2.6378899f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(track, 2f));
        var arrive = evaluator.PointSeconds(1);
        var still = evaluator.Keys[TrackEditing.PointKey(track, 1) + 1].Time - 2.0;

        for (var i = 1; i <= 16; i++)
            Assert.Equal(Facing(evaluator, arrive), Facing(evaluator, arrive + ((still - arrive) * i / 16)));
    }

    [Fact]
    public void DirectionOfTravelWithNoDirectionKeepsTheFirstPointsAim()
    {
        // Every point coincides, so the path has no direction: (-sin 1.1 cos 0.2, sin -0.2, -cos 1.1 cos 0.2).
        var evaluator = new TrackEvaluator(
            Build(
                [Point(3f, 3f, 3f, yaw: 1.1f, pitch: -0.2f), Point(3f, 3f, 3f), Point(3f, 3f, 3f)],
                AimMode.PathTangent
            )
        );

        Along(new Vector3(-0.87344255f, -0.19866933f, -0.44455440f), Facing(evaluator, 0.05), 1e-4f);
    }

    // A generated track as a scene file, so a failure prints something to paste into a test.
    private static string Print(Track track) => SceneJson.Write(new Scene([track], new HashSet<Guid>(), []));

    [Fact]
    public void TheAimNeverSteps()
    {
        // A smooth turn shrinks with the interval it's measured over; a step doesn't. Wherever the facing turns more
        // than 0.3° in 10 ms, halve the interval 8 times, keeping the half that turns more: a smooth turn falls to
        // about 1/256 of what it was, a step stays whole. Anything above a quarter is a step. The distance between
        // unit directions is 2·sin(θ/2), close enough to θ at these angles.
        AnyPathTrack.Sample(
            track =>
            {
                var evaluator = new TrackEvaluator(track);
                var target = AimTracker.AimPoint(track, null);
                float Turn(double a, double b) =>
                    Vector3.Distance(Facing(evaluator, a, target), Facing(evaluator, b, target));

                const double step = 0.01;
                for (var t = 0.0; t + step <= evaluator.Duration; t += step)
                {
                    var turn = Turn(t, t + step);
                    if (turn < 0.3f * Deg)
                        continue;
                    var (a, b) = (t, t + step);
                    for (var i = 0; i < 8; i++)
                    {
                        var m = (a + b) / 2;
                        (a, b) = Turn(a, m) >= Turn(m, b) ? (a, m) : (m, b);
                    }

                    var left = Turn(a, b);
                    if (left > turn / 4)
                        Assert.Fail(
                            $"The aim steps {left / Deg:0.###}° in {(b - a) * 1000:0.###} ms at {a:0.######} s of {evaluator.Duration:0.###} s"
                        );
                }
            },
            iter: 3000,
            print: Print
        );
    }

    [Fact]
    public void AHoldIsStill()
    {
        // Through a hold the camera stays exactly where it arrived, with the same field of view and roll. Its aim does
        // too, except that Direction of travel starts turning once the look ahead reaches past the hold's end.
        const int samples = 64;
        AnyPathTrack.Sample(
            track =>
            {
                var evaluator = new TrackEvaluator(track);
                var target = AimTracker.AimPoint(track, null);
                for (var point = 0; point < track.Points.Count; point++)
                {
                    if (TrackEditing.HoldSeconds(track, point) <= 0f)
                        continue;
                    double start = evaluator.PointSeconds(point);
                    double end = evaluator.Keys[TrackEditing.PointKey(track, point) + 1].Time;
                    var still = track.Aim == AimMode.PathTangent ? end - track.LookAhead : end;
                    var arrived = evaluator.Evaluate(start, target)!.Value;
                    for (var i = 0; i <= samples; i++)
                    {
                        var time = start + ((end - start) * i / samples);
                        var frame = evaluator.Evaluate(time, target)!.Value;
                        Assert.Equal(arrived.Position, frame.Position);
                        Assert.Equal(arrived.Fov, frame.Fov);
                        Assert.Equal(arrived.Roll, frame.Roll);
                        if (time <= still)
                            Assert.Equal(arrived.LookAt, frame.LookAt);
                    }
                }
            },
            iter: 5000,
            print: Print
        );
    }
}
