using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Spline;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class TrackAimTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.2f, -0.3f)]
    [InlineData(-2.7f, 0.6f)]
    [InlineData(3.0f, 0.9f)]
    public void FromDirectionInvertsFreeCamMotionDirection(float yaw, float pitch)
    {
        var direction = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));
        var (roundYaw, roundPitch) = TrackAim.FromDirection(direction);

        Assert.Equal(yaw, roundYaw, 4);
        Assert.Equal(pitch, roundPitch, 4);
    }

    [Fact]
    public void UnwrapAnglesTakesTheShortWayAcrossPlusMinus180()
    {
        var yaws = new[] { 170f * Deg, -170f * Deg };
        var unwrapped = TrackAim.UnwrapAngles(yaws);

        Assert.Equal(170f * Deg, unwrapped[0], 4);
        Assert.Equal(190f * Deg, unwrapped[1], 4);
        Assert.True(MathF.Abs(unwrapped[1] - unwrapped[0]) <= MathF.PI + 1e-4f);
    }

    [Fact]
    public void UnwrapAnglesLeavesASmallStepUntouched()
    {
        var yaws = new[] { 10f * Deg, 15f * Deg, 5f * Deg };
        var unwrapped = TrackAim.UnwrapAngles(yaws);

        Assert.Equal(10f * Deg, unwrapped[0], 4);
        Assert.Equal(15f * Deg, unwrapped[1], 4);
        Assert.Equal(5f * Deg, unwrapped[2], 4);
    }

    [Fact]
    public void UnwrapAnglesOfEmptySequenceIsEmpty() => Assert.Empty(TrackAim.UnwrapAngles(Array.Empty<float>()));

    [Fact]
    public void AimAlongAVerticalPathDirectionLooksStraightUp()
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 2, 0), new Vector3(0, 3, 0) };
        var table = new ArcLengthTable(points);

        var (_, pitch) = TrackAim.FromDirection(TrackAim.PathDirection(points, table, 1, 0.5f)!.Value);

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

        var actual = TrackAim.FromDirection(TrackAim.PathDirection(points, table, 0, 0.5f)!.Value);

        var expected = TrackAim.FromDirection(new Vector3(1, 0, 0));
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

        var (yaw, pitch) = TrackAim.FromDirection(TrackAim.PathDirection(points, table, 1, 0.5f)!.Value);

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

    // The last three points of a curved track from the game, where the path slows to nothing at its end.
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
        // Where the path's speed falls to zero its direction is rounding noise: a hair from the end once read 1.7° off.
        // A camera settling there must not flick, so the end and a hair from it agree to within 0.01°.
        var table = new ArcLengthTable(CurvedEnd);

        var (endYaw, endPitch) = TrackAim.FromDirection(
            TrackAim.PathDirection(CurvedEnd, table, segment, atEnd)!.Value
        );
        var (nearYaw, nearPitch) = TrackAim.FromDirection(
            TrackAim.PathDirection(CurvedEnd, table, segment, nearEnd)!.Value
        );

        Assert.Equal(endYaw, nearYaw, 0.01f * Deg);
        Assert.Equal(endPitch, nearPitch, 0.01f * Deg);
    }
}
