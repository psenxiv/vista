using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

namespace Vista.Tests.Tracks;

public class TrackEvaluatorTests
{
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
        var evaluator = new TrackEvaluator(TrackThrough(new[] { point }, aim, 2f));
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
        var track = TrackEditing.SetLegDuration(TrackThrough(points, speed: 2f), 1, 1f);
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
    public void RecordedAimPansOverALegBetweenTwoPointsOnOneSpotAndStopsAtItsEnd()
    {
        // Points at x = 0, 0 and 10, yaws 0°, 90° and 90°, the first leg pinned to 3 s. Distances: 0, 0.1 (the
        // coincident leg counts as MinTimingLength) and 10.1 (a straight 10-yalm leg, 2 s at speed 5).
        // Timing keys (0 s, 0), (3 s, 0.1), (5 s, 10.1), all Smooth: secants 0.1/3 = 0.033333 and 5 per second.
        // Key 0's end rule, EndWeights(3, 2) = (8/15, −0.3): 8/15·0.1 − 0.3·10 = −2.9467, clamped to 0.
        // Key 1's PCHIP slope, w = (2·2 + 3, 2 + 2·3) = (7, 8): 15 / (7/0.033333 + 8/5) = 15/211.6 = 0.070888 per second.
        // At 1.5 s (u = 0.5 of the 3 s leg): 0.5·0.1 − 0.125·(0.070888·3) = 0.05 − 0.026583 = 0.023417, still on the spot.
        // Yaw by distance: the leg after point 1 doesn't turn, so point 1's slope is 0. Point 0 takes its leg's own
        // 90°/0.1 = 900°/yalm, 90° over the leg. u = 0.023417/0.1 = 0.23417 gives h10 = 0.13734 and h01 = 0.13882:
        // 0.13734·90 + 0.13882·90 = 24.855° = 0.43379 rad.
        // Along the last leg both ends' slopes are 0 (no turn), so the yaw stays 90° = 1.5708 rad.
        var points = new[] { Point(0f, yaw: 0f), Point(0f, yaw: 90f * Deg), Point(10f, yaw: 90f * Deg) };
        var evaluator = new TrackEvaluator(TrackEditing.SetLegDuration(TrackThrough(points), 1, 3f));

        var state = evaluator.Evaluate(1.5)!.Value;

        Assert.Equal(Vector3.Zero, state.Position);
        Assert.Equal(0.43379f, CameraRotation.YawPitch(state.Forward).Yaw, 1e-4f);
        for (var t = 3.0; t <= 5.0; t += 0.25)
            Assert.Equal(1.5708f, CameraRotation.YawPitch(evaluator.Evaluate(t)!.Value.Forward).Yaw, 1e-4f);
    }

    [Fact]
    public void RecordedAimOnAnEasedLegTurnsWithTheTravel()
    {
        // One 10-yalm leg over 2 s, Ease In/Out: flat at both ends, so the distance is 10·smoothstep(u). A quarter of the
        // way through (0.5 s, u = 0.25): 10·(3·0.0625 − 2·0.015625) = 1.5625 yalms, 0.15625 of the leg. Two points blend
        // at one rate (the secant at both ends), so the yaw is 90°·0.15625 = 14.0625° = 0.24544 rad.
        var points = new[] { Point(0f, yaw: 0f), Point(10f, yaw: 90f * Deg) };
        var track = LegEasing.Set(TrackEditing.SetLegDuration(TrackThrough(points), 1, 2f), 1, Easing.EaseInOut);

        var state = new TrackEvaluator(track).Evaluate(0.5)!.Value;

        Assert.Equal(1.5625f, state.Position.X, 1e-4f);
        Assert.Equal(0.24544f, CameraRotation.YawPitch(state.Forward).Yaw, 1e-4f);
    }

    [Fact]
    public void RecordedAimHoldsStillWhileTheCameraHolds()
    {
        // Points at x = 0, 10 and 20 at speed 5: point 1 is reached at 2 s and held to 4 s, where the camera's distance
        // stays 10, point 1's own, so the yaw is point 1's 30° = 0.5236 rad throughout.
        var points = new[] { Point(0f, yaw: 0f), Point(10f, yaw: 30f * Deg), Point(20f, yaw: 90f * Deg) };
        var evaluator = new TrackEvaluator(TrackEditing.SetHold(TrackThrough(points), 1, 2f));

        for (var t = 2.0; t <= 4.0; t += 0.25)
            Assert.Equal(0.5236f, CameraRotation.YawPitch(evaluator.Evaluate(t)!.Value.Forward).Yaw, 1e-4f);
    }

    [Fact]
    public void PathTangentAimFollowsTheDirectionOfTravelOnAStraightLeg()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) };
        var track = TrackEditing.SetSpeed(TrackThrough(points, AimMode.PathTangent, 2f), 5f);
        var evaluator = new TrackEvaluator(track);

        var state = evaluator.Evaluate(2.0);
        Assert.NotNull(state);

        var expected = CameraRotation.YawPitch(new Vector3(1f, 0f, 0f));
        var actual = CameraRotation.YawPitch(state!.Value.Forward);

        Assert.Equal(expected.Yaw, actual.Yaw, 3);
        Assert.Equal(expected.Pitch, actual.Pitch, 3);
    }

    /// <summary>How far past its points' range a field of view may read, in radians: float round-off in the Hermite blend, a few 1.2e-7 steps of a value near 2.</summary>
    private const float FovRoundOff = 1e-5f;

    [Fact]
    public void AFieldOfViewThatRisesAndFallsBackStaysAtOrAboveItsLowestPoint()
    {
        // Field of view 1, 1, 2, 1, 1 at speed 10 over even 10-yalm legs: the timing's slopes all equal the secant, so
        // the camera is 10·t yalms along. Every point sits beside a leg that doesn't change or where the change turns
        // back, so every slope is 0, and each leg is a smoothstep between its ends: 1.5 halfway along leg 2 (1.5 s), and
        // exactly 1 along the flat leg 4 (3.335 s). Tangents within 0 to 3 times each leg's own change keep every leg
        // within its ends, so the whole shot stays between 1 and 2.
        var points = new[]
        {
            Point(0f, 0f, 0f, fov: 1f),
            Point(10f, 0f, 0f, fov: 1f),
            Point(20f, 0f, 0f, fov: 2f),
            Point(30f, 0f, 0f, fov: 1f),
            Point(40f, 0f, 0f, fov: 1f),
        };
        var evaluator = new TrackEvaluator(TrackEditing.SetSpeed(TrackThrough(points, speed: 2f), 10f));

        Assert.Equal(1.5f, evaluator.Evaluate(1.5)!.Value.Fov, 1e-4f);
        Assert.Equal(1f, evaluator.Evaluate(3.335)!.Value.Fov, 1e-5f);
        for (var t = 0.0; t <= evaluator.Duration; t += 0.01)
            Assert.InRange(evaluator.Evaluate(t)!.Value.Fov, 1f - FovRoundOff, 2f + FovRoundOff);
    }

    [Fact]
    public void AFieldOfViewThatRisesToAPlateauStaysAtOrBelowItsHighestPoint()
    {
        // Field of view 1, 2, 2, 1 at speed 10 over even 10-yalm legs, so the camera is 10·t yalms along. Points 1 and 2
        // sit beside the flat leg 2, so their slopes are 0; point 0 takes its leg's own 0.1 per yalm, 1 over the leg.
        // At 0.5 s, halfway along leg 1: Hermite(1, 2, 1, 0, 0.5) = 0.5 + 0.125 + 1 = 1.625. At 1 s the camera is at
        // point 1, and along leg 2 (1.5 s) it holds 2. The whole shot stays between 1 and 2.
        var points = new[]
        {
            Point(0f, 0f, 0f, fov: 1f),
            Point(10f, 0f, 0f, fov: 2f),
            Point(20f, 0f, 0f, fov: 2f),
            Point(30f, 0f, 0f, fov: 1f),
        };
        var evaluator = new TrackEvaluator(TrackEditing.SetSpeed(TrackThrough(points, speed: 2f), 10f));

        Assert.Equal(1.625f, evaluator.Evaluate(0.5)!.Value.Fov, 1e-4f);
        Assert.Equal(2f, evaluator.Evaluate(1.0)!.Value.Fov, 1e-4f);
        Assert.Equal(2f, evaluator.Evaluate(1.5)!.Value.Fov, 1e-4f);
        for (var t = 0.0; t <= evaluator.Duration; t += 0.01)
            Assert.InRange(evaluator.Evaluate(t)!.Value.Fov, 1f - FovRoundOff, 2f + FovRoundOff);
    }

    [Fact]
    public void AFieldOfViewPastTheEditorsRangePlaysAtItsEdge()
    {
        // The counterexample, simplified: a point three float steps under 5°, held 3 s, then 10 yalms at speed 2 to a
        // point recorded at 2.5 radians (about 143°). The channel runs from 5° to 120°: the leg starts at 3 s and ends at 8 s.
        var track = TrackEditing.SetHold(
            TrackThrough([Point(0f, fov: 0.087266445f), Point(10f, fov: 2.5f)], speed: 2f),
            0,
            3f
        );
        var evaluator = new TrackEvaluator(track);

        // In the hold, the first point's field of view raised to the editor's 5°.
        Assert.Equal(EditLimits.MinFov, evaluator.Evaluate(0.030769)!.Value.Fov, 0f);

        // Two points blend at one rate by distance, so the field of view is MinFov + (MaxFov − MinFov)·s/10 at s yalms along.
        // Timing keys (0 s, 0), (3 s, 0) and (8 s, 10): the hold's still interval zeroes the slope leaving it, and the last
        // key's end rule is EndWeights(5, 3)·(10, 0) = ((10 + 3)/(5·8))·10 = 3.25 yalms/s. Halfway along the leg, at 5.5 s,
        // Hermite(0, 10, 0, 3.25·5, 0.5) = 5 − 0.125·16.25 = 2.96875 yalms, so the field of view is
        // 0.0872665 + 0.296875·(2.0943951 − 0.0872665) = 0.0872665 + 0.5958663 = 0.6831328. Between the raw 0.087266445
        // and 2.5 it would be 0.8035467.
        Assert.Equal(0.6831328f, evaluator.Evaluate(5.5)!.Value.Fov, 1e-4f);

        // At the end, the last point's field of view lowered to the editor's 120°.
        Assert.Equal(EditLimits.MaxFov, evaluator.Evaluate(8.0)!.Value.Fov, 0f);
    }

    [Fact]
    public void ASinglePointPastTheEditorsRangePlaysAtItsEdge()
    {
        // A point recorded at 2.5 radians, about 143°, past the editor's 120°.
        var evaluator = new TrackEvaluator(TrackThrough([Point(0f, fov: 2.5f)], speed: 2f));

        Assert.Equal(EditLimits.MaxFov, evaluator.Evaluate(0.0)!.Value.Fov, 0f);
    }

    [Fact]
    public void LegLengthRefusesALegThatDoesntExist()
    {
        // Three points make legs 1 and 2.
        var evaluator = new TrackEvaluator(Build3PointTrack());

        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.LegLength(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.LegLength(3));
    }

    [Fact]
    public void WorldSpeedIsContinuousThroughAKeyBetweenUnequalSegments()
    {
        // Collinear points, so arc length is chord length: a 10 m leg then a 30 m leg, 5 s each.
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(40f, 0f, 0f) };
        var track = TrackEditing.SetLegDuration(
            TrackEditing.SetLegDuration(TrackThrough(points, speed: 2f), 1, 5f),
            2,
            5f
        );
        var evaluator = new TrackEvaluator(track);

        var at = evaluator.Evaluate(5.0)!.Value.Position;
        var (velocityBefore, velocityAfter) = Slopes(t => evaluator.Evaluate(t)!.Value.Position, 5.0, 1e-2);
        var (before, after) = (velocityBefore.Length(), velocityAfter.Length());

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
        var track = TrackEditing.SetSpeed(TrackThrough(points, speed: 2f), 4f);
        var evaluator = new TrackEvaluator(track);

        for (var i = 0; i <= 10; i++)
            Assert.Equal(4f * i, evaluator.Evaluate(i)!.Value.Position.X, 2);
    }

    [Fact]
    public void ALegBetweenCoincidentPointsStillTurnsOverItsDuration()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(10f, 0f, 0f, yaw: 90f * Deg) };
        var track = TrackEditing.SetLegDuration(TrackThrough(points, speed: 2f), 2, 5f);
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

    private static float YawAt(TrackEvaluator evaluator, double time) =>
        CameraRotation.YawPitch(evaluator.Evaluate(time)!.Value.Forward).Yaw;

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    public void RollBlendsBetweenPointsInEitherAimMode(AimMode aim)
    {
        var track = TrackEditing.SetLegDuration(
            TrackThrough(new[] { Point(0f, 0f, 0f, roll: 0f), Point(10f, 0f, 0f, roll: 90f * Deg) }, aim, 2f),
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
        var evaluator = new TrackEvaluator(TrackThrough(points, speed: 2f));

        // A frame's roll reads back wrapped to half a turn, so add up each step's turn the short way round.
        var previous = evaluator.Evaluate(0.0)!.Value.Roll;
        var turned = 0f;
        for (var t = 0.25; t <= 20.0; t += 0.25)
        {
            var roll = evaluator.Evaluate(t)!.Value.Roll;
            var step = Angles.Delta(previous, roll);
            Assert.True(step >= -1e-4f, $"roll went backwards at t={t}: {previous} -> {roll}");
            turned += step;
            previous = roll;
        }

        Assert.Equal(2f * MathF.PI, turned, 3);
    }

    [Fact]
    public void ASinglePointTrackKeepsItsRoll()
    {
        var track = TrackThrough(new[] { Point(1f, 2f, 3f, roll: 0.3f) }, speed: 2f);

        // Roll reads back through a rotation, so within float round-off.
        Assert.Equal(0.3f, new TrackEvaluator(track).Evaluate(0.0)!.Value.Roll, 1e-6f);
    }

    // Points on a line at x = 0, 10, 20; Linear keys at 0, 5, 10 s.
    private static TrackEvaluator StraightLinear()
    {
        var points = new[] { 0f, 10f, 20f }.Select(x => new ControlPoint(new Vector3(x, 0f, 0f), 0f, 0f, 1f)).ToArray();
        var track = TrackThrough(points, speed: 2f);
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
        var evaluator = new TrackEvaluator(TrackThrough(new[] { point }, speed: 2f));
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
            TrackThrough(new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) }, aim, 2f),
            5f
        );
        var target = new Vector3(10f, 5f, -30f);

        AimsAt(target, new TrackEvaluator(track).Evaluate(2.0, target)!.Value, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.WatchTarget)]
    public void WithNoTargetTheNewModesUseTheRecordedAim(AimMode aim)
    {
        var points = new[] { Point(0f, 0f, 0f, yaw: 90f * Deg), Point(10f, 0f, 0f, yaw: 90f * Deg) };

        var state = new TrackEvaluator(TrackThrough(points, aim, 2f)).Evaluate(1.0)!.Value;

        Assert.Equal(90f * Deg, CameraRotation.YawPitch(state.Forward).Yaw, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.WatchTarget)]
    public void ASinglePointTrackTurnsToATarget(AimMode aim)
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f, fov: 1.2f, roll: 0.3f);

        var state = new TrackEvaluator(TrackThrough(new[] { point }, aim, 2f))
            .Evaluate(0.0, new Vector3(1f, 2f, -7f))!
            .Value;

        var (yaw, pitch) = CameraRotation.YawPitch(state.Forward);
        Assert.Equal(point.Position, state.Position);
        Assert.Equal(0f, yaw, 4);
        Assert.Equal(0f, pitch, 4);
        Assert.Equal(1.2f, state.Fov);
        // Roll reads back through a rotation, so within float round-off.
        Assert.Equal(0.3f, state.Roll, 1e-6f);
    }

    [Fact]
    public void ATargetOnTheCameraLeavesTheRecordedAim()
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f);

        var state = new TrackEvaluator(TrackThrough(new[] { point }, AimMode.LookAt, 2f))
            .Evaluate(0.0, new Vector3(1.05f, 2f, 3f))!
            .Value;

        Assert.Equal(FreeCamMotion.LookAtFrom(point.Position, 0.5f, 0.1f), state.LookAt);
    }

    // The direction the camera faces at time t.
    private static Vector3 Facing(TrackEvaluator evaluator, double t) => evaluator.Evaluate(t)!.Value.Forward;

    [Fact]
    public void LookingAheadOnAStraightPathFacesAlongIt()
    {
        var evaluator = new TrackEvaluator(TrackThrough([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent, 2f));

        Near(Vector3.UnitX, Facing(evaluator, 1.0), 1e-3f);
        Near(Vector3.UnitX, Facing(evaluator, evaluator.Duration - 0.1), 1e-3f);
        Near(Vector3.UnitX, Facing(evaluator, evaluator.Duration), 1e-3f);
    }

    [Fact]
    public void LookingAheadFacesWhereThePathIsThatFarOn()
    {
        // Looking as far ahead as the first leg is long, at the start the camera faces point 1 exactly: (4, 0, 2) away.
        var track = TrackThrough([Point(0f), Point(4f, z: 2f), Point(8f, z: -8f)], AimMode.PathTangent, 2f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(track, new TrackEvaluator(track).LegLength(1)));

        Near(Vector3.Normalize(new Vector3(4f, 0f, 2f)), Facing(evaluator, 0.0), 1e-3f);
    }

    [Fact]
    public void LookingAheadNothingFacesStraightAlongThePath()
    {
        // Symmetric about the middle point, the path runs parallel to x there. Looking as far ahead as the last leg is
        // long instead faces the last point: (4, 0, -4) away.
        var track = TrackThrough([Point(-4f), Point(0f, z: 4f), Point(4f)], AimMode.PathTangent, 2f);
        var evaluator = new TrackEvaluator(track);
        var middle = evaluator.PointSeconds(1);

        Near(Vector3.UnitX, Facing(new TrackEvaluator(TrackEditing.SetLookAhead(track, 0f)), middle), 1e-3f);
        Near(
            Vector3.Normalize(new Vector3(1f, 0f, -1f)),
            Facing(new TrackEvaluator(TrackEditing.SetLookAhead(track, evaluator.LegLength(2))), middle),
            1e-3f
        );
    }

    [Fact]
    public void LookingAheadSnapsRoundWhereThePathRunsStraightBackAlongItself()
    {
        // Out to x = 10 and back to 5 at 2 yalms a second, the path turns round at 10 at 5 s: the camera is at 2t and
        // the spot 2 yalms ahead at 20 − (2t + 2). They pass at 4.5 s, so the facing flips from +x to −x there at once.
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(TrackThrough([Point(0f), Point(10f), Point(5f)], AimMode.PathTangent, 2f), 2f)
        );

        Near(Vector3.UnitX, Facing(evaluator, 4.49), 1e-3f);
        Near(-Vector3.UnitX, Facing(evaluator, 4.51), 1e-3f);

        // The snap turns about the current up, so the picture stays upright.
        Near(Vector3.UnitY, evaluator.Evaluate(4.51)!.Value.Up, 1e-4f);
    }

    [Theory]
    [InlineData(1e-3)]
    [InlineData(4e-3)]
    [InlineData(1.6e-2)]
    public void LookingAheadStaysSteadyJustBeforeTheEndFarFromTheOrigin(double early)
    {
        // Far from the origin, just before the end the spot is carried on past it, 2.5 yalms ahead the way the path
        // finishes, so the look matches the look at the very end to within a degree (cos 1° ≈ 0.99985).
        var track = TrackThrough(
            [Point(612f, 42f, -488f), Point(620f, 42f, -480f), Point(631f, 43f, -489f)],
            AimMode.PathTangent,
            2f
        );
        var evaluator = new TrackEvaluator(track);

        Assert.InRange(
            Vector3.Dot(Facing(evaluator, evaluator.Duration - early), Facing(evaluator, evaluator.Duration)),
            0.99985f,
            1.0001f
        );
    }

    [Fact]
    public void LookingAheadHoldsItsLookThroughAHeldCorner()
    {
        // A corner, (0, 0, 0), (5, 0, 0), (5, 0, 5) at 5 yalms a second, held 3 s at the middle, looking as far ahead as the
        // last leg is long: through the hold the spot is the last point, (0, 0, 5) from the held camera, and it stays put.
        var track = TrackEditing.SetHold(
            TrackThrough([Point(0f), Point(5f), Point(5f, z: 5f)], AimMode.PathTangent),
            1,
            3f
        );
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(track, new TrackEvaluator(track).LegLength(2)));
        var arrive = evaluator.PointSeconds(1);
        var depart = evaluator.Keys[2].Time;
        var held = evaluator.Evaluate(arrive)!.Value;

        Near(Vector3.UnitZ, held.Forward, 1e-4f);
        for (var i = 0; i <= 64; i++)
            Assert.Equal(held.Forward, Facing(evaluator, arrive + ((depart - arrive) * i / 64)));

        // A millisecond either side of the hold the camera eases through about 1e-5 yalm, and the spot the same distance
        // along the path, seen from about 5 yalms away: about 4e-6 rad. 0.15° allows that many times over. The distance
        // between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
        Assert.InRange(Vector3.Distance(Facing(evaluator, arrive - 1e-3), held.Forward), 0f, 0.15f * Deg);
        Assert.InRange(Vector3.Distance(Facing(evaluator, depart + 1e-3), held.Forward), 0f, 0.15f * Deg);
    }

    [Fact]
    public void TheLookAheadSpotIsTheLookAheadsDistanceAlongThePath()
    {
        // Points at x = 0, 10, 20 at 2 yalms a second: every leg's secant is 2, so the camera runs x = 2t. At 2 s it is at
        // 4, and the spot 3 yalms on is at 7.
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(TrackThrough([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent, 2f), 3f)
        );

        Near(new Vector3(7f, 0f, 0f), evaluator.LookAheadSpot(2.0)!.Value, 1e-3f);
    }

    [Fact]
    public void PastTheEndTheLookAheadSpotCarriesOnStraightTheWayThePathFinishes()
    {
        // As above, at 9 s the camera is at 18, and 3 yalms on is 1 past the end at 20. The reflected end makes the last
        // leg straight, so the path finishes along +x and the spot carries on to (21, 0, 0), straight ahead.
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(TrackThrough([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent, 2f), 3f)
        );

        Near(new Vector3(21f, 0f, 0f), evaluator.LookAheadSpot(9.0)!.Value, 1e-3f);
        Near(Vector3.UnitX, Facing(evaluator, 9.0), 1e-3f);
        Near(Vector3.UnitX, Facing(evaluator, evaluator.Duration), 1e-3f);
    }

    [Fact]
    public void WhereTheSpotIsOnTheCameraTheCameraFacesAlongThePath()
    {
        // Out from −8 to 7 and back to −3, all on the x axis: 15 yalms out, then back. Centripetal Catmull-Rom's tangent at
        // the turn-back point is √15 − 5/(√15 + √10) − √10 = 0, so the path stops at 7 and turns straight back. At 1 yalm
        // a second (equal secants, so distance is time) the camera is 14 yalms along at 14 s, at (6, 0, 0), and the spot
        // 2 yalms on is 16 along, back at (6, 0, 0) on the camera. There the camera faces along the path, +x.
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(TrackThrough([Point(-8f), Point(7f), Point(-3f)], AimMode.PathTangent, 1f), 2f)
        );
        var frame = evaluator.Evaluate(14.0)!.Value;

        Near(new Vector3(6f, 0f, 0f), frame.Position, 1e-3f);
        Near(new Vector3(6f, 0f, 0f), evaluator.LookAheadSpot(14.0)!.Value, 1e-3f);
        Near(Vector3.UnitX, frame.Forward, 1e-3f);
        Near(Vector3.UnitY, frame.Up, 1e-3f);

        // 0.2 ms later the camera is at 6.0002 and the spot at 5.9998: 4e-4 yalm away, behind it (−x), but under the
        // 1e-3 yalm that counts as on the camera, so the camera still faces along the path, +x. 1e-4 on the gap keeps it
        // behind and under 1e-3 whatever the arc-length table's rounding.
        var justAfter = evaluator.Evaluate(14.0002)!.Value;
        Near(new Vector3(-4e-4f, 0f, 0f), evaluator.LookAheadSpot(14.0002)!.Value - justAfter.Position, 1e-4f);
        Near(Vector3.UnitX, justAfter.Forward, 1e-3f);

        // 2 ms later the spot is 4e-3 behind the camera, over 1e-3, so the camera faces it, −x.
        Near(-Vector3.UnitX, Facing(evaluator, 14.002), 1e-3f);
    }

    [Fact]
    public void ATrackWithNoPointsHasNoLookAheadSpot()
    {
        // Direction of travel with the default 2.5-yalm look ahead, but no path to look along.
        Assert.Null(new TrackEvaluator(TrackEditing.Empty(AimMode.PathTangent)).LookAheadSpot(1.0));
    }

    [Fact]
    public void LookingAheadAlongPointsAllOnOneSpotKeepsTheFirstPointsAim()
    {
        // Three points at (1, 2, 3): each leg counts as MinTimingLength for timing, but has no length or direction, so
        // the path finishes no way and the spot 2.5 yalms on is the point itself, on the camera. With no direction to face,
        // the camera keeps point 0's aim: yaw 0 and pitch 0 face −z.
        var point = Point(1f, 2f, 3f);
        var evaluator = new TrackEvaluator(
            TrackEditing.SetLookAhead(TrackThrough([point, point, point], AimMode.PathTangent, 2f), 2.5f)
        );

        foreach (var time in new[] { 0.0, evaluator.Duration / 2, evaluator.Duration })
        {
            Assert.Equal(point.Position, evaluator.LookAheadSpot(time)!.Value);
            var frame = evaluator.Evaluate(time)!.Value;
            Assert.Equal(point.Position, frame.Position);
            Near(-Vector3.UnitZ, frame.Forward, 1e-6f);
        }
    }

    [Fact]
    public void TheLookAheadSpotStaysPutWhileTheCameraHolds()
    {
        // As above, held 2 s at x = 10: the camera stays at 10 from 5 s to 7 s, so the spot 3 yalms on stays at (13, 0, 0).
        var track = TrackThrough([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent, 2f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(TrackEditing.SetHold(track, 1, 2f), 3f));

        foreach (var time in new[] { 5.0, 6.0, 7.0 })
            Near(new Vector3(13f, 0f, 0f), evaluator.LookAheadSpot(time)!.Value, 1e-3f);
    }

    [Fact]
    public void ThereIsNoLookAheadSpotWithoutALookAheadOrWhenNotAimingAlongThePath()
    {
        var points = new[] { Point(0f), Point(10f) };

        Assert.Null(new TrackEvaluator(TrackThrough(points)).LookAheadSpot(0.5));
        Assert.Null(
            new TrackEvaluator(TrackEditing.SetLookAhead(TrackThrough(points, AimMode.PathTangent), 0f)).LookAheadSpot(
                0.5
            )
        );
    }

    [Fact]
    public void LookingAheadSettlesOnTheFinalDirectionIntoAHoldAtTheLastPoint()
    {
        // The last leg runs straight along +z (points 2 and 3 and the end's reflection in a line), holding 2 s at its end.
        // Half a second before arriving the spot 1 yalm on is on that leg or carried on past its end along it, so the camera
        // faces +z; through the hold the spot stays 1 yalm past the end, and the camera keeps facing +z.
        var track = TrackThrough([Point(0f), Point(10f), Point(10f, z: 10f), Point(10f, z: 20f)], AimMode.PathTangent);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(TrackEditing.SetHold(track, 3, 2f), 1f));
        var arrive = evaluator.PointSeconds(3);

        Near(Vector3.UnitZ, Facing(evaluator, arrive - 0.5), 1e-5f);
        Near(Vector3.UnitZ, Facing(evaluator, arrive + 1.0), 1e-5f);
        Near(Vector3.UnitZ, Facing(evaluator, evaluator.Duration), 1e-5f);
    }

    [Fact]
    public void LookingAheadNothingFacesThePathThroughAHeldCorner()
    {
        // The held corner with no look ahead: the camera faces the path's own direction at the corner throughout. Its points
        // are equally far apart, so the centripetal knots are evenly spaced and the tangent there is (p2 - p0) / 2, along
        // (10, 0, 10).
        var track = TrackEditing.SetHold(
            TrackThrough([Point(0f), Point(10f), Point(10f, z: 10f)], AimMode.PathTangent),
            1,
            3f
        );
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(track, 0f));
        var arrive = evaluator.PointSeconds(1);

        Near(Vector3.Normalize(new Vector3(1f, 0f, 1f)), Facing(evaluator, arrive), 1e-5f);
        Near(Vector3.Normalize(new Vector3(1f, 0f, 1f)), Facing(evaluator, arrive + 3.0), 1e-5f);
    }

    [Fact]
    public void LookingAheadTurnsSmoothlyIntoAndOutOfAHold()
    {
        // Point 2 holds for 2 s. Leaving it, the turn peaks near 30 deg/s, 0.03° a millisecond; snapping between the path's
        // own direction and the spot ahead at 0.1 yalm stepped 1.7° at once. 0.1° allows the turn three times over.
        var track = TrackEditing.SetHold(
            TrackThrough([Point(-10f), Point(0f, z: 10f), Point(10f)], AimMode.PathTangent, 2f),
            1,
            2f
        );
        var run = new Run(track);
        var arrive = run.Arrive(1);

        // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
        Assert.InRange(run.LargestTurn(Millisecond, arrive - 0.5, arrive + 2.5), 0f, 0.1f * Deg);
    }

    [Fact]
    public void LookingAheadAcrossAHairpinFacesTheSpotAhead()
    {
        // The path turns back 0.2 yalm from itself. At 2 yalms a second, equal secants make distance 2t, so at 1.5 s the
        // spot 4 yalms further along the path is where the camera will be at 3.5 s, and the camera faces straight at it,
        // though it's under a yalm away across the gap.
        var track = TrackEditing.SetLookAhead(
            TrackThrough([Point(0f), Point(5f), Point(5f, z: 0.2f), Point(0f, z: 0.2f)], AimMode.PathTangent, 2f),
            4f
        );
        var evaluator = new TrackEvaluator(track);

        AimsAt(evaluator.Evaluate(3.5)!.Value.Position, evaluator.Evaluate(1.5)!.Value, 4);
    }

    [Fact]
    public void LookingAheadSettlesIntoTheDemosLastPointWithoutAStep()
    {
        // East Hawker, looking 10 yalms ahead, eases into its last point turning at most 0.0033° a frame at 60 fps in its last
        // half second, read from the run since a recorded track's turn can't be worked by hand. Snapping to the exact
        // tangent 0.1 yalm out once stepped 0.12° in one frame; 0.05° allows the steady turn and catches the step.
        var track = DemoScene().Tracks.Single(t => t.Name == "East Hawker fly through");
        var run = new Run(track);
        var end = run.Arrive(track.Points.Count - 1);

        // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
        Assert.InRange(run.LargestTurn(FrameSeconds, end - 0.5, end + 0.5), 0f, 0.05f * Deg);
    }

    [Fact]
    public void RecordedAimTurnsAtOneRateThroughAPointBetweenLegsOfDifferentTimes()
    {
        // Legs of 10 and 5 yalms at 5 a second take 2 s and 1 s; yaw 0, 1, 3 gives (1/2·1 + 2/1·2) / 3 = 1.5 rad/s at the middle.
        var evaluator = new TrackEvaluator(
            TrackThrough([Point(0f, yaw: 0f), Point(10f, yaw: 1f), Point(15f, yaw: 3f)], speed: 5f)
        );
        float Yaw(double time) => CameraRotation.YawPitch(Facing(evaluator, time)).Yaw;
        var (left, right) = Slopes(Yaw, 2.0, 1e-3);

        Assert.Equal(1.5f, left, 0.02f);
        Assert.Equal(1.5f, right, 0.02f);
    }

    [Fact]
    public void RecordedAimHoldsStillThroughAHold()
    {
        // Point 2 holds for 2 s: through the hold the camera keeps point 2's yaw, 1.
        var track = TrackEditing.SetHold(
            TrackThrough([Point(0f, yaw: 0f), Point(10f, yaw: 1f), Point(20f, yaw: 2f)], speed: 2f),
            1,
            2f
        );
        var evaluator = new TrackEvaluator(track);
        var arrive = evaluator.PointSeconds(1);

        Assert.Equal(1f, CameraRotation.YawPitch(Facing(evaluator, arrive + 1.9)).Yaw, 1e-4f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(TrackEditing.DefaultLookAhead)]
    public void DirectionOfTravelTurnsSmoothlyAsThePathDriftsAcrossTheVertical(float lookAhead)
    {
        // The path climbs 10 yalms, bowing 0.2 to +x and back, so its direction passes the vertical from +x to -x. The facing
        // follows the bow, well under 0.05° a millisecond, and up turns once through the vertical passage, so the picture
        // never whips.
        var run = new Run(
            TrackEditing.SetLookAhead(
                TrackThrough([Point(0f), Point(0.2f, 5f), Point(0f, 10f)], AimMode.PathTangent, 2f),
                lookAhead
            )
        );

        // The distance between two unit directions is 2·sin(θ/2), within 1e-7 of θ at these angles.
        Assert.InRange(run.LargestTurn(Millisecond), 0f, 0.05f * Deg);
        Assert.InRange(run.LargestTwist(), 0f, PictureSpinLimit);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(TrackEditing.DefaultLookAhead)]
    public void DirectionOfTravelLooksStraightUpThroughACraneShotWithoutSpinning(float lookAhead)
    {
        // Along -z, straight up, then along +x. FromDirection's yaw is atan2(-x, -z): 0 heading -z and -π/2 heading +x.
        // The first leg lies in the plane x = 0 and the last two in z = -10, so the headings are exact there.
        var track = TrackEditing.SetLookAhead(
            TrackThrough(
                [
                    Point(0f),
                    Point(0f, 0f, -10f),
                    Point(0f, 10f, -10f),
                    Point(0f, 20f, -10f),
                    Point(10f, 20f, -10f),
                    Point(20f, 20f, -10f),
                ],
                AimMode.PathTangent,
                2f
            ),
            lookAhead
        );
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(0f, CameraRotation.YawPitch(Facing(evaluator, 2.0)).Yaw, 1e-4f);
        Assert.Equal(-MathF.PI / 2f, CameraRotation.YawPitch(Facing(evaluator, evaluator.Duration - 2.0)).Yaw, 1e-4f);
        Assert.InRange(new Run(track).LargestTwist(), 0f, PictureSpinLimit);

        // The way out (+x) is a quarter turn from the way in (-z), short of 135°, so the passage turns the picture a quarter
        // and it comes out upright. The last leg lies straight and level (points 3 to 5 in a line), facing +x, where upright
        // is (0, 1, 0).
        Near(Vector3.UnitY, evaluator.Evaluate(evaluator.Duration)!.Value.Up, 1e-5f);
    }

    [Fact]
    public void DirectionOfTravelFacesStraightUpAtTheMiddleOfAStraightClimb()
    {
        // Points 1 to 3 stand in a vertical line, so Catmull-Rom's tangent at point 2, (point 3 - point 1) / 2, is straight up,
        // with no cap on the pitch any more.
        var evaluator = new TrackEvaluator(
            TrackThrough(
                [Point(0f), Point(0f, 0f, -10f), Point(0f, 10f, -10f), Point(0f, 20f, -10f), Point(10f, 20f, -10f)],
                AimMode.PathTangent,
                2f
            ) with
            {
                LookAhead = 0f,
            }
        );

        Near(Vector3.UnitY, Facing(evaluator, evaluator.PointSeconds(2)), 1e-4f);
    }

    [Fact]
    public void DirectionOfTravelThatIsVerticalThroughoutTakesItsUpFromTheFirstPointsYaw()
    {
        // Straight up from the start: there's no upright to start from, so the picture's top points along the first
        // point's heading at yaw 0.3, (sin 0.3, 0, cos 0.3), as FromAngles gives at pitch 90°.
        var evaluator = new TrackEvaluator(
            TrackThrough([Point(0f, yaw: 0.3f), Point(0f, 10f)], AimMode.PathTangent, 2f)
        );
        var frame = evaluator.Evaluate(1.0)!.Value;

        Near(Vector3.UnitY, Facing(evaluator, 1.0), 1e-4f);
        Near(new Vector3(MathF.Sin(0.3f), 0f, MathF.Cos(0.3f)), frame.Up, 1e-4f);
    }

    [Fact]
    public void DirectionOfTravelIsUpsideDownAtTheTopOfALoopAndUprightAfter()
    {
        // A loop in the plane z = 0, as the regression scene's. At its top point, (0, 16, 0), Catmull-Rom's tangent is
        // ((-7, 10) - (7, 10)) / 2, straight along -x, and the way out of the climb's vertical passage reversed the way
        // in, so up is the level up inverted: (0, -1, 0). Down the far side the second passage rights it, and along the
        // last leg, straight along +x (points 7 to 9 in a line), up is (0, 1, 0). The look-at point sits 10 yalms along the facing.
        var track = TrackEditing.SetLookAhead(
            TrackThrough(
                [
                    Point(-20f),
                    Point(-6f),
                    Point(4f, 3f),
                    Point(7f, 10f),
                    Point(0f, 16f),
                    Point(-7f, 10f),
                    Point(-4f, 3f),
                    Point(6f),
                    Point(20f),
                    Point(34f),
                ],
                AimMode.PathTangent,
                2f
            ),
            0f
        );
        var evaluator = new TrackEvaluator(track);
        var top = evaluator.Evaluate(evaluator.PointSeconds(4))!.Value;

        Near(-Vector3.UnitX, Facing(evaluator, evaluator.PointSeconds(4)), 1e-5f);
        Near(-Vector3.UnitY, top.Up, 1e-5f);
        Assert.Equal(FreeCamMotion.LookAtDistance, Vector3.Distance(top.Position, top.LookAt), 1e-4f);
        Near(Vector3.UnitY, evaluator.Evaluate(evaluator.Duration)!.Value.Up, 1e-5f);
    }

    [Fact]
    public void DirectionOfTravelHasNoStepOnATrackThatSweptThroughTheVertical()
    {
        // Found by TheAimNeverSteps as a 1.86° step at 7.63 s, when the aim was capped at 89° and its yaw read from the
        // sideways part: the facing is now the path's own, so there's no step, and up turns once through each vertical
        // passage, so no whip.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 4f,
        };
        foreach (
            var point in new[]
            {
                Point(21.121212f, 0f, -22.063553f),
                Point(1f, 5f, 0f),
                Point(0f, 1f, -0.9508197f),
                Point(22.12416f, 0f, 20.54599f),
                Point(-20f, 0f, -26.857143f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 1, 0.07317073f), 3, 1.3629642f);
        track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(track, 2, 4.5509834f), 3, 53.612453f);
        var run = new Run(TrackEditing.SetLookAhead(track, 1.1391547f));

        Assert.Empty(run.FacingSteps());
        Assert.InRange(run.LargestTwist(), 0f, PictureSpinLimit);
    }

    [Fact]
    public void DirectionOfTravelDoesNotFlipDivingPastStraightDownAndBack()
    {
        // Found by TheAimNeverSteps as a 180° flip at 1.06 s, carrying up: the path dives and doubles back, and its look
        // ahead sweeps the facing within 10° of straight down and out again in 4 ms.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 19.566769f,
        };
        foreach (
            var point in new[]
            {
                Point(0f, 1.8947369f, 8.947952e-37f, yaw: 2.0040665f, fov: 0.2745092f, roll: -1f),
                Point(-22.105576f, -2.72549f, 0f, yaw: 1f, pitch: -1.2068965f, roll: 0.20150566f),
                Point(-5f, 0.42857143f, 0f, yaw: -1f, pitch: 1f, fov: 2f, roll: 2f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(track, 2, 2.6231241f);
        Assert.Empty(new Run(TrackEditing.SetLookAhead(track, 0.19565217f)).UpSteps());
    }

    [Fact]
    public void DirectionOfTravelDoesNotFlipWhereTheFacingWhipsThroughStraightUpBetweenSamples()
    {
        // Found by TheAimNeverSteps as a 180° flip at 2.64 s: the look ahead swings the facing through straight up at
        // about 10,000° a second, across the whole vertical passage between two 10 ms samples. Sampling finer where the
        // facing turns fast finds the passage, so the picture turns through it rather than flipping.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 17f,
        };
        foreach (
            var point in new[]
            {
                Point(0f, -5f, -2.718872f, yaw: 1f, pitch: 0.98134375f, fov: 0.22727273f, roll: -2.6455696f),
                Point(0f, 1.3033708f, 16f, yaw: -1.6803432f, fov: 0.8271605f, roll: -0.10169491f),
                Point(0f, -3.0555556f, 2f, pitch: -1f, roll: -3f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 0, 2.1590958f), 1, 0.5309508f);
        Assert.Empty(new Run(TrackEditing.SetLookAhead(track, 1.9551187f)).UpSteps());
    }

    [Fact]
    public void DirectionOfTravelKeepsLevelAsTheViewWhipsRoundADoubleback()
    {
        // Found by TheAimNeverSteps as an 11.2° picture step at 3.595 s: at a 177° doubleback the look ahead whips the
        // facing round about 180° in a millisecond, through 69° up. Keeping level, the picture turns with it, at most 1.6
        // times as far: a turn that follows the view, not a step.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 13f,
        };
        foreach (
            var point in new[]
            {
                Point(-10.25f, -3.3181818f, 16f, pitch: -1f, roll: -2.5263157f),
                Point(-26.304348f, -3.2342892f, -9f, fov: 0.6905582f),
                Point(0f, -3f, 29.419434f, yaw: -0.32635975f, fov: 0.5496135f, roll: -0.28850555f),
                Point(11.000916f, 0.88461536f, -21.178062f, pitch: -9.447e-25f, fov: 2f, roll: 2f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 0, 3f), 1, 1f);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 2, 2f), 3, 0.44615385f);
        track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(track, 1, 44.570385f), 3, 26.346441f);
        Assert.Empty(new Run(TrackEditing.SetLookAhead(track, 1.3770492f)).PictureSteps());
    }

    [Fact]
    public void DirectionOfTravelStartsFacingTheWayALapWithinTheLookAheadFinishes()
    {
        // A lap back to its start, about 7 yalms round, under the 10-yalm look ahead: at 0 s the spot is past the end,
        // carried on the way the path finishes. The last leg runs from (2, 0, -2) to the origin, straight with its end's
        // reflection, so the camera faces along (-1, 0, 1)/√2, and leaves without a step.
        var track = TrackEditing.Empty(AimMode.PathTangent);
        foreach (
            var point in new[] { Point(0f), Point(0f, 0f, -1f), Point(0f, 0f, -2f), Point(2f, 0f, -2f), Point(0f) }
        )
            track = TrackEditing.Append(track, point);
        for (var leg = 1; leg <= 4; leg++)
            track = TrackEditing.SetLegDuration(track, leg, 0.25f);
        var run = new Run(TrackEditing.SetLookAhead(TrackEditing.SetHold(track, 4, 2f), TrackEditing.MaxLookAhead));

        Near(new Vector3(-0.70710677f, 0f, 0.70710677f), run.At(0.0).Forward, 1e-5f);
        Assert.Empty(run.FacingSteps());
    }

    [Fact]
    public void DirectionOfTravelDoesNotTurnRoundLeavingAHoldWhereThePathEnds()
    {
        // Found by TheAimNeverSteps as a 114.6° step at 3 s, when the spot waited at the end of a path that ends where it
        // starts, held 3 s there. Looking 1.93 yalms ahead, the spot stays ahead of the camera as it leaves.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 12f,
        };
        foreach (var point in new[] { Point(0f), Point(-3f, -1.3278688f), Point(0f, 3.4775333f, -5f), Point(0f) })
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 0, 3f), 1, 2f);
        track = TrackEditing.SetHold(track, 3, 2.099152f);
        Assert.Empty(new Run(TrackEditing.SetLookAhead(track, 1.9320359f)).FacingSteps());
    }

    [Fact]
    public void LookAtStartingStraightUnderItsPointDoesNotWhip()
    {
        // Found by ThePictureNeverWhips as a 7.6° excess in a frame: starting 0.1° from straight under the point, the
        // picture started level for a heading that meant nothing and turned half round in the rest of the passage. With no
        // picture before it, it now starts as it leaves the passage.
        var track = TrackEditing.Empty(AimMode.LookAt) with
        {
            Speed = 16.666666f,
        };
        foreach (
            var point in new[]
            {
                Point(0f, -0.38341713f, 0f, yaw: 0.065789476f, fov: 0.5749512f),
                Point(3.9448682e-38f, -5.8340003e-24f, -15.807693f, pitch: -0.31578946f, fov: 2f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetLookAt(TrackEditing.SetLookAhead(track, 0f), new Vector3(0f, 15f, 0f));

        Assert.InRange(new Run(track).LargestTwist(), 0f, PictureSpinLimit);
    }

    [Fact]
    public void DirectionOfTravelKeepsItsUpExactlyThroughAHoldReachedLeavingAVerticalPassage()
    {
        // Found by AHoldIsStill as a last-bit change in up just after arriving at point 1's hold: the facing leaves a vertical
        // passage as the camera stops, and the passage's end is sampled within a millisecond after, so the arrival was still
        // in its turn. Through a hold the up is now read at the hold's start.
        var track = TrackEditing.Empty(AimMode.PathTangent) with
        {
            Speed = 14f,
        };
        foreach (
            var point in new[]
            {
                Point(13.3f, 0f, 4.5208335f),
                Point(-17f, 0f, -9.875137f),
                Point(-17.950653f, -4f, -11f),
                Point(27f, -3.5347633f, 0f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetHold(TrackEditing.SetHold(track, 0, 0.39784947f), 1, 3f);
        track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(track, 1, 6.582359f), 3, 11.641592f);
        var evaluator = new TrackEvaluator(TrackEditing.SetLookAhead(track, 0.49757695f));
        var arrive = evaluator.PointSeconds(1);

        Assert.Equal(evaluator.Evaluate(arrive)!.Value.Up, evaluator.Evaluate(arrive + 1.0)!.Value.Up);
    }

    [Fact]
    public void LookAtKeepsItsUpExactlyToTheEndOfAHold()
    {
        // Found by AHoldIsStill as a last-bit change in up at the end of point 0's hold, where the camera travels on:
        // settling for no time at all still squared the up again. The up is now level there, from the same facing.
        var track = TrackEditing.Empty(AimMode.LookAt) with
        {
            Speed = 12.873444f,
        };
        foreach (
            var point in new[]
            {
                Point(-28.297297f, -4f, 24.88421f, yaw: 3f, pitch: 1f),
                Point(1f, 0f, -15.846154f, yaw: -3.005597f, pitch: -1f, fov: 0.79591835f, roll: 2f),
            }
        )
            track = TrackEditing.Append(track, point);
        track = TrackEditing.SetLegSpeed(TrackEditing.SetHold(track, 0, 0.6363636f), 1, 21.570787f);
        track = TrackEditing.SetLookAt(TrackEditing.SetLookAhead(track, 0.7317672f), new Vector3(0f, 15f, 0f));
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(
            evaluator.Evaluate(0.0, track.LookAt)!.Value.Up,
            evaluator.Evaluate(evaluator.Keys[1].Time, track.LookAt)!.Value.Up
        );
    }

    [Fact]
    public void LookAtPassesStraightUnderItsPointWithoutFlipping()
    {
        // The camera runs along x from -10 to 30 under a Look At point at (0, 10, 0), reaching x = 0 at 5 s (10 yalms at 2
        // a second). Looking up-and-ahead, upright leans back along -x; looking up-and-back past it, along +x. Look At never
        // inverts, so the picture turns half round about the vertical over the passage's turn span: from the first point,
        // 45° up, to where the view leaves 60° from straight up at x = 10 / tan 30° = 17.32, 150° from +x, short of the
        // last point. Straight under the point the view has turned 45° of those 105°: share 3/7, eased 3s² - 2s³ = 135/343,
        // θ = 70.845°, so up is the lean (-cos θ, 0, ±sin θ) = (-0.32812, 0, ±0.94464). The span's end is found within 0.78
        // ms, where the view turns 2·10/400 = 0.05 rad a second, 4e-5 rad of 1.83, so 1e-4 covers it.
        var track = TrackEditing.SetLookAt(
            TrackThrough([Point(-10f), Point(0f), Point(30f)], AimMode.LookAt, 2f),
            new Vector3(0f, 10f, 0f)
        );
        var run = new Run(track);
        var under = run.At(run.Arrive(1));

        Near(Vector3.UnitY, under.Forward, 1e-4f);
        Assert.Equal(-0.32812f, under.Up.X, 1e-4f);
        Assert.Equal(0.94464f, MathF.Abs(under.Up.Z), 1e-4f);
        Assert.InRange(run.LargestTwist(), 0f, PictureSpinLimit);
        var end = run.At(run.Duration);
        // At the end, at (30, 0, 0) facing (-30, 10, 0)/√1000, upright leans back: (10, 30, 0)/√1000.
        Near(new Vector3(10f, 30f, 0f) / MathF.Sqrt(1000f), end.Up, 1e-5f);
    }

    [Fact]
    public void LookAtThatIsVerticalThroughoutTakesItsUpFromTheFirstPointsYaw()
    {
        // Rising straight up under the Look At point (0, 10, 0), always facing straight up: there's no upright, so the
        // picture's top points along the first point's heading at yaw 0.3, (sin 0.3, 0, cos 0.3), as FromAngles gives
        // at pitch 90°.
        var track = TrackEditing.SetLookAt(
            TrackThrough([Point(0f, -10f, yaw: 0.3f), Point(0f, 0f)], AimMode.LookAt, 2f),
            new Vector3(0f, 10f, 0f)
        );
        var evaluator = new TrackEvaluator(track);

        Near(new Vector3(MathF.Sin(0.3f), 0f, MathF.Cos(0.3f)), evaluator.Evaluate(1.0, track.LookAt)!.Value.Up, 1e-5f);
    }

    [Fact]
    public void AnAimPointOtherThanTheLookAtPointStaysUpright()
    {
        // The Look At track passes under its point (0, 10, 0) at x = 0; aimed instead at (1, 10, 0) there, the camera
        // faces (1, 10, 0)/√101 and stays upright: square to the facing in the x-y plane, leaning back, (-10, 1, 0)/√101.
        var track = TrackEditing.SetLookAt(
            TrackThrough([Point(-10f), Point(0f), Point(30f)], AimMode.LookAt, 2f),
            new Vector3(0f, 10f, 0f)
        );
        var evaluator = new TrackEvaluator(track);
        var frame = evaluator.Evaluate(evaluator.PointSeconds(1), new Vector3(1f, 10f, 0f))!.Value;

        Near(new Vector3(-10f, 1f, 0f) / MathF.Sqrt(101f), frame.Up, 1e-5f);
    }

    [Fact]
    public void LookAtOrbitingItsPointStaysUpright()
    {
        // Circling 10 yalms out at 30° below a Look At point, never near straight up: the picture stays exactly upright, as
        // watching always looked. At point i the camera is at angle θ = i·45° round the ring, facing in and 30° up,
        // (-cos θ cos 30°, sin 30°, -sin θ cos 30°), whose upright up leans back: (cos θ sin 30°, cos 30°, sin θ sin 30°).
        var ring = Enumerable
            .Range(0, 8)
            .Select(i => Point(10f * MathF.Cos(i * MathF.PI / 4f), 0f, 10f * MathF.Sin(i * MathF.PI / 4f)));
        var track = TrackEditing.SetLookAt(
            TrackThrough(ring, AimMode.LookAt, 2f),
            new Vector3(0f, 10f * MathF.Tan(30f * Deg), 0f)
        );
        var evaluator = new TrackEvaluator(track);

        for (var i = 1; i <= 6; i++)
        {
            var theta = i * MathF.PI / 4f;
            var expected = new Vector3(MathF.Cos(theta) * 0.5f, MathF.Cos(30f * Deg), MathF.Sin(theta) * 0.5f);
            Near(expected, evaluator.Evaluate(evaluator.PointSeconds(i), track.LookAt)!.Value.Up, 1e-5f);
        }
    }

    [Fact]
    public void DirectionOfTravelWithNoDirectionKeepsTheFirstPointsAim()
    {
        // Every point coincides, so the path has no direction: (-sin 1.1 cos 0.2, sin -0.2, -cos 1.1 cos 0.2).
        var evaluator = new TrackEvaluator(
            TrackThrough(
                [Point(3f, 3f, 3f, yaw: 1.1f, pitch: -0.2f), Point(3f, 3f, 3f), Point(3f, 3f, 3f)],
                AimMode.PathTangent,
                2f
            )
        );

        Near(new Vector3(-0.87344255f, -0.19866933f, -0.44455440f), Facing(evaluator, 0.05), 1e-4f);
    }

    /// <summary>The most frames a property samples from one track, so a run of zero steps still ends.</summary>
    private const int FrameBudget = 5000;

    [Fact]
    [Trait("Category", "Property")]
    public void EveryFrameOfAPathTrackIsWellFormed()
    {
        Gen.Select(AnyPathTrack, AnyFrameStep.Array[1, 32])
            .Sample(
                (track, steps) =>
                {
                    var run = new Run(track);
                    AssertEveryFrameWellFormed(steps, FrameBudget, run.Clock());
                    AssertWellFormed(run.At(run.Duration), "At the end");
                },
                iter: 1000,
                print: Kept<(Track Track, float[] Steps)>(x =>
                    $"{PrintTrack(x.Track)}\nSteps: {string.Join(", ", x.Steps)}"
                )
            );
    }

    [Fact]
    [Trait("Category", "Property")]
    public void TheAimNeverSteps()
    {
        AnyPathTrack.Sample(
            track =>
            {
                var run = new Run(track);
                var steps = run.FacingSteps();
                if (steps.Count > 0)
                    Assert.Fail(
                        $"The aim steps {steps[0].Size / Deg:0.###}° at {steps[0].Time:0.######} s of {run.Duration:0.###} s"
                    );
                var flips = run.PictureSteps();
                if (flips.Count > 0)
                    Assert.Fail(
                        $"The picture steps {flips[0].Size / Deg:0.###}° at {flips[0].Time:0.######} s of {run.Duration:0.###} s"
                    );
            },
            iter: 3000,
            print: Kept<Track>(PrintTrack)
        );
    }

    [Fact]
    [Trait("Category", "Property")]
    public void ThePictureNeverWhips()
    {
        // Where the camera decides which way is up (Direction of travel and Look At), the picture turns about its centre
        // no faster than the spin limit beyond what keeping level asks of it, however it passes straight up or down.
        // The points' roll is taken off, and Recorded aim left out, since how fast the picture rolls there is the user's.
        AnyPathTrack
            .Where(track => track.Aim != AimMode.AimKeys)
            .Select(track => track with { Points = [.. track.Points.Select(p => p with { Roll = 0f })] })
            .Sample(
                track =>
                {
                    var run = new Run(track);
                    var twist = run.LargestTwist();
                    if (!(twist <= PictureSpinLimit))
                        Assert.Fail($"The picture turns {twist / Deg:0.###}° in a frame of {run.Duration:0.###} s");
                },
                iter: 1000,
                print: Kept<Track>(PrintTrack)
            );
    }

    [Fact]
    [Trait("Category", "Property")]
    public void AHoldIsStill()
    {
        // Through a hold the camera stays exactly where it arrived, with its aim, the picture's up and its field of view.
        const int samples = 64;
        AnyPathTrack.Sample(
            track =>
            {
                var run = new Run(track);
                for (var point = 0; point < track.Points.Count; point++)
                {
                    if (TrackEditing.HoldSeconds(track, point) <= 0f)
                        continue;
                    var (start, end) = (run.Arrive(point), run.Depart(point));
                    var arrived = run.At(start);
                    // Equal treats NaN as equal to NaN, so the frame held to must be finite for the checks below to mean anything.
                    AssertWellFormed(arrived, $"the hold at point {point}");
                    for (var i = 0; i <= samples; i++)
                    {
                        var time = start + ((end - start) * i / samples);
                        var frame = run.At(time);
                        Assert.Equal(arrived.Position, frame.Position);
                        Assert.Equal(arrived.Fov, frame.Fov);
                        Assert.Equal(arrived.LookAt, frame.LookAt);
                        Assert.Equal(arrived.Up, frame.Up);
                    }
                }
            },
            iter: 5000,
            print: Kept<Track>(PrintTrack)
        );
    }

    // Build3PointTrack's keys are at 0, 5 and 10 s; a 2 s hold on point 1 adds a key at 7 and moves point 2's to 12.

    [Fact]
    public void ALegSpansFromItsStartKeyToItsEndKey()
    {
        var evaluator = new TrackEvaluator(Build3PointTrack());
        var held = new TrackEvaluator(TrackEditing.SetHold(Build3PointTrack(), 1, 2f));

        Assert.Equal(0f, evaluator.LegSpan(1).Start, 1e-4f);
        Assert.Equal(5f, evaluator.LegSpan(1).End, 1e-4f);
        Assert.Equal(5f, evaluator.LegSpan(2).Start, 1e-4f);
        Assert.Equal(10f, evaluator.LegSpan(2).End, 1e-4f);
        Assert.Equal(7f, held.LegSpan(2).Start, 1e-4f);
        Assert.Equal(12f, held.LegSpan(2).End, 1e-4f);
    }
}
