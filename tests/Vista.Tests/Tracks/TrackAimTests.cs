using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

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
        // Endpoints duplicate as phantoms, so both tangents are (10 - 0) / 2 = 5 and the Hermite midpoint is exactly 5.
        var values = new[] { 0f, 10f };

        Assert.Equal(5f, TrackAim.Channel(values, 0, 0.5f), 5);
    }

    [Fact]
    public void ChannelRejectsAnOutOfRangeSegment()
    {
        var values = new[] { 1f, 2f, 3f };
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(values, -1, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(values, 2, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(new[] { 1f }, 0, 0.5f));
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
    public void TowardAimsFromOnePlaceAtAnother()
    {
        var ahead = TrackAim.Toward(Vector3.Zero, new Vector3(0f, 0f, -10f))!.Value;
        Assert.Equal(0f, ahead.Yaw, 4);
        Assert.Equal(0f, ahead.Pitch, 4);

        var left = TrackAim.Toward(Vector3.Zero, new Vector3(-10f, 0f, 0f))!.Value;
        Assert.Equal(90f * Deg, left.Yaw, 4);
    }

    [Fact]
    public void TowardClampsPitchAndGivesNoAimForATargetOnTheCamera()
    {
        Assert.Equal(TrackAim.PitchLimit, TrackAim.Toward(Vector3.Zero, new Vector3(0f, 10f, 0f))!.Value.Pitch, 5);
        Assert.Null(TrackAim.Toward(Vector3.Zero, new Vector3(0.05f, 0f, 0f)));
    }
}
