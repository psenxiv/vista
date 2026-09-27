using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Spline;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class TrackAimTests
{
    [Fact]
    public void AimAlongAVerticalPathDirectionLooksStraightUp()
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 2, 0), new Vector3(0, 3, 0) };
        var table = new ArcLengthTable(points);

        var (_, pitch) = CameraRotation.YawPitch(TrackAim.PathDirection(points, table, 1, 0.5f)!.Value);

        // Every point has x = z = 0, so the direction is exactly +y: pitch π/2, with no cap.
        Assert.Equal(MathF.PI / 2f, pitch, 1e-6f);
    }

    [Fact]
    public void UsableRefusesADirectionShorterThanAMillionthAndPassesALongerOneUnchanged()
    {
        Assert.Null(TrackAim.Usable(Vector3.Zero));
        Assert.Null(TrackAim.Usable(new Vector3(0f, 9e-7f, 0f)));
        Assert.Equal(new Vector3(1e-5f, 0f, 0f), TrackAim.Usable(new Vector3(1e-5f, 0f, 0f)));
    }

    [Fact]
    public void PathDirectionFallsBackToTheNearestValidDirectionAcrossACollapsedSegment()
    {
        // Segment 0 (points 0-1) collapses to a single point; segments 1 and 2 continue in a straight line
        // along +X, so the nearest valid direction is unambiguous regardless of exactly where it is sampled.
        var points = new[] { new Vector3(5, 0, 0), new Vector3(5, 0, 0), new Vector3(10, 0, 0), new Vector3(15, 0, 0) };
        var table = new ArcLengthTable(points);

        var actual = CameraRotation.YawPitch(TrackAim.PathDirection(points, table, 0, 0.5f)!.Value);

        var expected = CameraRotation.YawPitch(new Vector3(1, 0, 0));
        Assert.Equal(expected.Yaw, actual.Yaw, 4);
        Assert.Equal(expected.Pitch, actual.Pitch, 4);
    }

    [Fact]
    public void PathDirectionInACollapsedSegmentBetweenTwoOthersKeepsTheWayItCameIn()
    {
        // Segment 1 collapses at (10, 0, 0), between segment 0 along +X and segment 2 along -Z. The collapsed segment
        // has no length, so both neighbours are 0 away along the path and the earlier one, the way in, wins: +X,
        // whose yaw is atan2(-1, -0) = -90°.
        var points = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(10, 0, -10),
        };
        var table = new ArcLengthTable(points);

        var (yaw, pitch) = CameraRotation.YawPitch(TrackAim.PathDirection(points, table, 1, 0.5f)!.Value);

        Assert.Equal(-90f * Deg, yaw, 1e-4f);
        Assert.Equal(0f, pitch, 1e-4f);
    }

    [Fact]
    public void PathDirectionIsNullWhenEveryPointCoincides()
    {
        var points = new[] { new Vector3(3, 3, 3), new Vector3(3, 3, 3), new Vector3(3, 3, 3), new Vector3(3, 3, 3) };
        var table = new ArcLengthTable(points);

        Assert.Null(TrackAim.PathDirection(points, table, 1, 0.5f));
    }

    [Fact]
    public void TowardAimsFromOnePlaceAtAnother()
    {
        var ahead = TrackAim.Toward(Vector3.Zero, new Vector3(0f, 0f, -10f))!.Value;
        Assert.Equal(0f, ahead.Yaw, 4);
        Assert.Equal(0f, ahead.Pitch, 4);

        var left = TrackAim.Toward(Vector3.Zero, new Vector3(-10f, 0f, 0f))!.Value;
        Assert.Equal(90f * Deg, left.Yaw, 4);
    }

    [Fact]
    public void TowardLooksStraightUpAtATargetOverheadAndGivesNoAimForOneOnTheCamera()
    {
        // The target is straight above, so the pitch is π/2, with no cap.
        Assert.Equal(MathF.PI / 2f, TrackAim.Toward(Vector3.Zero, new Vector3(0f, 10f, 0f))!.Value.Pitch, 5);
        Assert.Null(TrackAim.Toward(Vector3.Zero, new Vector3(0.05f, 0f, 0f)));
    }

    // The last three points of a curved track from the game.
    private static readonly Vector3[] CurvedEnd =
    [
        new(-158.79079f, 19.590088f, 25.831505f),
        new(-166.89452f, 20.147827f, 35.720806f),
        new(-178.6489f, 19.898912f, 41.992096f),
    ];

    [Theory]
    [InlineData(1, 1f, 0.9999999f)]
    [InlineData(1, 1f, 0.99999f)]
    [InlineData(0, 0f, 0.0000001f)]
    [InlineData(0, 0f, 0.00001f)]
    public void PathDirectionHoldsSteadyAtEitherEndOfThePath(int segment, float atEnd, float nearEnd)
    {
        // A camera settling at either end must not flick, so the end and a hair from it agree to within 0.01°.
        var table = new ArcLengthTable(CurvedEnd);

        var (endYaw, endPitch) = CameraRotation.YawPitch(
            TrackAim.PathDirection(CurvedEnd, table, segment, atEnd)!.Value
        );
        var (nearYaw, nearPitch) = CameraRotation.YawPitch(
            TrackAim.PathDirection(CurvedEnd, table, segment, nearEnd)!.Value
        );

        Assert.Equal(endYaw, nearYaw, 0.01f * Deg);
        Assert.Equal(endPitch, nearPitch, 0.01f * Deg);
    }

    // Recorded aim uses the points' aim always, Watch Target when its character is lost and Follow Target when it doesn't look at them; Direction of travel and Look At never do.

    [Theory]
    [InlineData(AimMode.AimKeys, true)]
    [InlineData(AimMode.WatchTarget, true)]
    [InlineData(AimMode.FollowTarget, true)]
    [InlineData(AimMode.PathTangent, false)]
    [InlineData(AimMode.LookAt, false)]
    public void OnlyModesThatCanUseAPointsAimLetItBeEdited(AimMode aim, bool expected) =>
        Assert.Equal(expected, TrackAim.UsesPointAim(aim));

    // A target exactly MinTargetDistance (0.1 yalms, straight ahead along −z) away is far enough to aim at: yaw 0, pitch 0.
    [Fact]
    public void ATargetExactlyTheShortestDistanceAwayGivesAnAim() =>
        Assert.Equal((0f, 0f), TrackAim.Toward(Vector3.Zero, new Vector3(0f, 0f, -TrackAim.MinTargetDistance)));

    [Fact]
    public void TheDirectionExactlyAtThePathsStartIsItsFirstLeg()
    {
        Vector3[] points = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, 10f)];
        var table = new ArcLengthTable(points);

        // The derivative at the start is p₁ − p₀ = (10, 0, 0) (CatmullRomTests), read at the start itself with no nudge.
        Near(new Vector3(10f, 0f, 0f), TrackAim.PathDirection(points, table, 0, 0f)!.Value, 1e-4f);
    }

    [Fact]
    public void AStartOnCoincidentPointsTakesTheNearestDirection()
    {
        Vector3[] points = [new(0f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, 10f)];
        var table = new ArcLengthTable(points);

        // Segment 0 has no length, so its direction comes from segment 1, (0,0,0) to (10,0,0) with d0 = 0 and d1 = d2 = √10.
        // Its start tangent is √10·[−(10,0,0)/√10 + (10,0,0)/√10] = 0, so it is read a nudge inside, at t = 1e-3. Its end
        // tangent is √10·[(10,0,0)/√10 − (10,0,10)/(2√10) + (0,0,10)/√10] = (5, 0, 5), and the Hermite slope there is
        // (6t − 6t²)·(10,0,0) + (3t² − 2t)·(5,0,5) = (50t − 45t², 0, 15t² − 10t) = (0.049955, 0, −0.009985). Starting from
        // rest it leans away from the bend, about 11.3° off +x: unit (0.980603, 0, −0.196003).
        var direction = TrackAim.PathDirection(points, table, 0, 0f)!.Value;
        Near(new Vector3(0.980603f, 0f, -0.196003f), Vector3.Normalize(direction), 1e-4f);
    }
}
