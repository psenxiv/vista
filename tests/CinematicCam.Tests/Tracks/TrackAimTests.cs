using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class TrackAimTests
{
    private const float Deg = MathF.PI / 180f;

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
    public void UnwrapAnglesOfEmptySequenceIsEmpty()
        => Assert.Empty(TrackAim.UnwrapAngles(Array.Empty<float>()));

    [Fact]
    public void ChannelPassesThroughItsValuesAtSegmentEndpoints()
    {
        var values = new[] { 1f, 4f, -2f, 6f };

        for (var segment = 0; segment < values.Length - 1; segment++)
        {
            Assert.Equal(values[segment], TrackAim.Channel(values, segment, 0f), 5);
            Assert.Equal(values[segment + 1], TrackAim.Channel(values, segment, 1f), 5);
        }
    }

    [Fact]
    public void ChannelInterpolatesBetweenItsEndpoints()
    {
        var values = new[] { 0f, 10f };
        var mid = TrackAim.Channel(values, 0, 0.5f);

        Assert.True(mid is > 0f and < 10f, $"expected midpoint between 0 and 10, got {mid}");
    }

    [Fact]
    public void PathTangentClampsPitchOnANearVerticalPath()
    {
        var points = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 2, 0),
            new Vector3(0, 3, 0),
        };
        var table = new ArcLengthTable(points);

        var (_, pitch) = TrackAim.PathTangent(points, table, 1, 0.5f, (0f, 0f));

        Assert.True(pitch <= TrackAim.PitchLimit + 1e-4f, $"pitch {pitch} exceeds the clamp");
    }

    [Fact]
    public void PathTangentFallsBackToTheNearestValidDirectionAcrossACollapsedSegment()
    {
        // Segment 0 (points 0-1) collapses to a single point; segments 1 and 2 continue in a straight line
        // along +X, so the nearest valid direction is unambiguous regardless of exactly where it is sampled.
        var points = new[]
        {
            new Vector3(5, 0, 0),
            new Vector3(5, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(15, 0, 0),
        };
        var table = new ArcLengthTable(points);

        var actual = TrackAim.PathTangent(points, table, 0, 0.5f, (99f, 99f));

        var expected = TrackAim.FromDirection(new Vector3(1, 0, 0));
        Assert.Equal(expected.Yaw, actual.Yaw, 4);
        Assert.Equal(expected.Pitch, actual.Pitch, 4);
    }

    [Fact]
    public void PathTangentReturnsFallbackWhenEveryPointCoincides()
    {
        var points = new[]
        {
            new Vector3(3, 3, 3),
            new Vector3(3, 3, 3),
            new Vector3(3, 3, 3),
            new Vector3(3, 3, 3),
        };
        var table = new ArcLengthTable(points);
        var fallback = (Yaw: 1.1f, Pitch: -0.2f);

        var actual = TrackAim.PathTangent(points, table, 1, 0.5f, fallback);

        Assert.Equal(fallback.Yaw, actual.Yaw);
        Assert.Equal(fallback.Pitch, actual.Pitch);
    }

    [Fact]
    public void PathTangentIsDeterministicRegardlessOfCallOrder()
    {
        var points = new[]
        {
            new Vector3(5, 0, 0),
            new Vector3(5, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(15, 0, 0),
        };
        var table = new ArcLengthTable(points);
        var fallback = (Yaw: 0f, Pitch: 0f);

        var first = TrackAim.PathTangent(points, table, 0, 0.5f, fallback);
        _ = TrackAim.PathTangent(points, table, 2, 0.7f, fallback);
        var second = TrackAim.PathTangent(points, table, 0, 0.5f, fallback);

        Assert.Equal(first.Yaw, second.Yaw, 5);
        Assert.Equal(first.Pitch, second.Pitch, 5);
    }
}
