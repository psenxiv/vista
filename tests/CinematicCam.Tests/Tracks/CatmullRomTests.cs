using System.Numerics;
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

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
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void SegmentCountIsZeroBelowTheMinimumPointCount(int pointCount, bool loop)
        => Assert.Equal(0, CatmullRom.SegmentCount(pointCount, loop));

    [Fact]
    public void SegmentCountForATwoPointLoopIsTwo()
        => Assert.Equal(2, CatmullRom.SegmentCount(2, loop: true));

    [Fact]
    public void SegmentCountForOpenTrackIsOneLessThanPointCount()
        => Assert.Equal(4, CatmullRom.SegmentCount(5, loop: false));

    [Fact]
    public void SegmentCountForLoopEqualsPointCount()
        => Assert.Equal(5, CatmullRom.SegmentCount(5, loop: true));

    [Fact]
    public void CurvePassesThroughEveryControlPointOnAnOpenTrack()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length, loop: false);
        for (var segment = 0; segment < segments; segment++)
        {
            AssertClose(FivePoints[segment], CatmullRom.Evaluate(FivePoints, loop: false, segment, 0f));
            AssertClose(FivePoints[segment + 1], CatmullRom.Evaluate(FivePoints, loop: false, segment, 1f));
        }
    }

    [Fact]
    public void CurvePassesThroughEveryControlPointOnALoop()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length, loop: true);
        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % FivePoints.Length;
            AssertClose(FivePoints[segment], CatmullRom.Evaluate(FivePoints, loop: true, segment, 0f));
            AssertClose(FivePoints[next], CatmullRom.Evaluate(FivePoints, loop: true, segment, 1f));
        }
    }

    [Fact]
    public void LoopLastSegmentRunsFromLastPointBackToFirst()
    {
        var lastSegment = CatmullRom.SegmentCount(FivePoints.Length, loop: true) - 1;
        AssertClose(FivePoints[^1], CatmullRom.Evaluate(FivePoints, loop: true, lastSegment, 0f));
        AssertClose(FivePoints[0], CatmullRom.Evaluate(FivePoints, loop: true, lastSegment, 1f));
    }

    [Fact]
    public void TwoPointsIsAStraightDolly()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 4, -6);
        var points = new[] { a, b };

        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            var p = CatmullRom.Evaluate(points, loop: false, 0, t);
            var toPoint = p - a;
            var toEnd = b - a;
            var cross = Vector3.Cross(toPoint, toEnd);
            Assert.True(cross.Length() < 0.001f, $"t={t}: {p} is not on the line from {a} to {b}");
        }

        AssertClose(a, CatmullRom.Evaluate(points, loop: false, 0, 0f));
        AssertClose(b, CatmullRom.Evaluate(points, loop: false, 0, 1f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DegenerateZeroOrOnePointsGiveNoSegments(int pointCount)
    {
        var points = new Vector3[pointCount];
        Assert.Equal(0, CatmullRom.SegmentCount(points.Length, loop: false));
        Assert.Equal(0, CatmullRom.SegmentCount(points.Length, loop: true));
    }

    [Fact]
    public void EvaluatingASegmentWithNoPointsThrowsArgumentOutOfRange()
    {
        var points = Array.Empty<Vector3>();
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Evaluate(points, loop: false, 0, 0.5f));
    }

    [Fact]
    public void EvaluatingASegmentPastSegmentCountThrowsArgumentOutOfRange()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length, loop: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Evaluate(FivePoints, loop: false, segments, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CatmullRom.Derivative(FivePoints, loop: false, -1, 0.5f));
    }

    [Fact]
    public void TwoCoincidentPointsDoNotThrowOrProduceNaN()
    {
        var p = new Vector3(3, 3, 3);
        var points = new[] { p, p };

        var value = CatmullRom.Evaluate(points, loop: false, 0, 0.5f);
        AssertClose(p, value);
        Assert.False(float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z));

        var deriv = CatmullRom.Derivative(points, loop: false, 0, 0.5f);
        Assert.False(float.IsNaN(deriv.X) || float.IsNaN(deriv.Y) || float.IsNaN(deriv.Z));
    }

    [Fact]
    public void AllCoincidentPointsDoNotThrowOrProduceNaN()
    {
        var p = new Vector3(-2, 5, 1);
        var points = new[] { p, p, p, p };

        var segments = CatmullRom.SegmentCount(points.Length, loop: false);
        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0f; t <= 1f; t += 0.25f)
            {
                var value = CatmullRom.Evaluate(points, loop: false, segment, t);
                AssertClose(p, value);
                var deriv = CatmullRom.Derivative(points, loop: false, segment, t);
                Assert.False(float.IsNaN(deriv.X) || float.IsNaN(deriv.Y) || float.IsNaN(deriv.Z));
            }
        }
    }

    [Fact]
    public void OneOfFourPointsCoincidingWithANeighbourDoesNotThrow()
    {
        var points = new[] { FivePoints[0], FivePoints[0], FivePoints[1], FivePoints[2] };
        var segments = CatmullRom.SegmentCount(points.Length, loop: false);
        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0f; t <= 1f; t += 0.25f)
            {
                var value = CatmullRom.Evaluate(points, loop: false, segment, t);
                Assert.False(float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z));
            }
        }
    }

    [Fact]
    public void DerivativeMatchesAFiniteDifference()
    {
        const float h = 0.0005f;
        var segments = CatmullRom.SegmentCount(FivePoints.Length, loop: false);

        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0.1f; t <= 0.9f; t += 0.2f)
            {
                var analytic = CatmullRom.Derivative(FivePoints, loop: false, segment, t);
                var plus = CatmullRom.Evaluate(FivePoints, loop: false, segment, t + h);
                var minus = CatmullRom.Evaluate(FivePoints, loop: false, segment, t - h);
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
