using System.Numerics;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class CatmullRomTests
{
    private static readonly Vector3[] FivePoints =
    {
        new(0, 0, 0),
        new(1, 2, 0),
        new(4, 2, 1),
        new(5, -1, 0),
        new(8, 0, -2),
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SegmentCountIsZeroBelowTheMinimumPointCount(int pointCount)
        => Assert.Equal(0, CatmullRom.SegmentCount(pointCount));

    [Fact]
    public void SegmentCountForOpenTrackIsOneLessThanPointCount()
        => Assert.Equal(4, CatmullRom.SegmentCount(5));

    [Fact]
    public void CurvePassesThroughEveryControlPointOnAnOpenTrack()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length);
        for (var segment = 0; segment < segments; segment++)
        {
            AssertClose(FivePoints[segment], CatmullRom.Evaluate(FivePoints, segment, 0f));
            AssertClose(FivePoints[segment + 1], CatmullRom.Evaluate(FivePoints, segment, 1f));
        }
    }

    [Fact]
    public void TwoPointsIsAStraightDolly()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 4, -6);
        var points = new[] { a, b };

        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            var p = CatmullRom.Evaluate(points, 0, t);
            var toPoint = p - a;
            var toEnd = b - a;
            var cross = Vector3.Cross(toPoint, toEnd);
            Assert.True(cross.Length() < 0.001f, $"t={t}: {p} is not on the line from {a} to {b}");
        }

        AssertClose(a, CatmullRom.Evaluate(points, 0, 0f));
        AssertClose(b, CatmullRom.Evaluate(points, 0, 1f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DegenerateZeroOrOnePointsGiveNoSegments(int pointCount)
    {
        var points = new Vector3[pointCount];
        Assert.Equal(0, CatmullRom.SegmentCount(points.Length));
    }

    [Fact]
    public void EvaluatingASegmentWithNoPointsThrowsArgumentOutOfRange()
    {
        var points = Array.Empty<Vector3>();
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Evaluate(points, 0, 0.5f));
    }

    [Fact]
    public void EvaluatingASegmentPastSegmentCountThrowsArgumentOutOfRange()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Evaluate(FivePoints, segments, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Derivative(FivePoints, -1, 0.5f));
    }

    [Fact]
    public void TwoCoincidentPointsDoNotThrowOrProduceNaN()
    {
        var p = new Vector3(3, 3, 3);
        var points = new[] { p, p };

        var value = CatmullRom.Evaluate(points, 0, 0.5f);
        AssertClose(p, value);
        Assert.False(float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z));

        var deriv = CatmullRom.Derivative(points, 0, 0.5f);
        Assert.False(float.IsNaN(deriv.X) || float.IsNaN(deriv.Y) || float.IsNaN(deriv.Z));
    }

    [Fact]
    public void AllCoincidentPointsDoNotThrowOrProduceNaN()
    {
        var p = new Vector3(-2, 5, 1);
        var points = new[] { p, p, p, p };

        var segments = CatmullRom.SegmentCount(points.Length);
        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0f; t <= 1f; t += 0.25f)
            {
                var value = CatmullRom.Evaluate(points, segment, t);
                AssertClose(p, value);
                var deriv = CatmullRom.Derivative(points, segment, t);
                Assert.False(float.IsNaN(deriv.X) || float.IsNaN(deriv.Y) || float.IsNaN(deriv.Z));
            }
        }
    }

    [Fact]
    public void OneOfFourPointsCoincidingWithANeighbourDoesNotThrow()
    {
        var points = new[] { FivePoints[0], FivePoints[0], FivePoints[1], FivePoints[2] };
        var segments = CatmullRom.SegmentCount(points.Length);
        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0f; t <= 1f; t += 0.25f)
            {
                var value = CatmullRom.Evaluate(points, segment, t);
                Assert.False(float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z));
            }
        }
    }

    [Fact]
    public void DerivativeMatchesAFiniteDifference()
    {
        const float h = 0.0005f;
        var segments = CatmullRom.SegmentCount(FivePoints.Length);

        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0.1f; t <= 0.9f; t += 0.2f)
            {
                var analytic = CatmullRom.Derivative(FivePoints, segment, t);
                var plus = CatmullRom.Evaluate(FivePoints, segment, t + h);
                var minus = CatmullRom.Evaluate(FivePoints, segment, t - h);
                var finite = (plus - minus) / (2f * h);

                Assert.True((analytic - finite).Length() < 0.01f,
                    $"segment {segment} t={t}: analytic {analytic}, finite difference {finite}");
            }
        }
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }
}
