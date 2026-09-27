using System.Numerics;
using Vista.Core.Tracks.Spline;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Spline;

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
    public void SegmentCountIsZeroBelowTheMinimumPointCount(int pointCount) =>
        Assert.Equal(0, CatmullRom.SegmentCount(pointCount));

    [Fact]
    public void SegmentCountForOpenTrackIsOneLessThanPointCount() => Assert.Equal(4, CatmullRom.SegmentCount(5));

    [Fact]
    public void CurvePassesThroughEveryControlPointOnAnOpenTrack()
    {
        var segments = CatmullRom.SegmentCount(FivePoints.Length);
        for (var segment = 0; segment < segments; segment++)
        {
            Near(FivePoints[segment], CatmullRom.Evaluate(FivePoints, segment, 0f), 5e-4f);
            Near(FivePoints[segment + 1], CatmullRom.Evaluate(FivePoints, segment, 1f), 5e-4f);
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

        Near(a, CatmullRom.Evaluate(points, 0, 0f), 5e-4f);
        Near(b, CatmullRom.Evaluate(points, 0, 1f), 5e-4f);
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
        Near(p, value, 5e-4f);
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
                Near(p, value, 5e-4f);
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
        var segments = CatmullRom.SegmentCount(FivePoints.Length);

        for (var segment = 0; segment < segments; segment++)
        {
            for (var t = 0.1f; t <= 0.9f; t += 0.2f)
            {
                var analytic = CatmullRom.Derivative(FivePoints, segment, t);
                var (left, right) = Slopes(u => CatmullRom.Evaluate(FivePoints, segment, (float)u), t, 5e-4);
                var finite = (left + right) / 2f;

                Assert.True(
                    (analytic - finite).Length() < 0.01f,
                    $"segment {segment} t={t}: analytic {analytic}, finite difference {finite}"
                );
            }
        }
    }

    [Fact]
    public void ThePathLeavesItsFirstPointAlongItsFirstLegAndReachesItsLastAlongItsLast()
    {
        Vector3[] points = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, 10f)];

        // The point invented before the first is (-10, 0, 0), as far behind it as the second is ahead, so the knot intervals
        // there are both √10 and the tangent is √10·[(10,0,0)/√10 − (20,0,0)/(2√10) + (10,0,0)/√10] = (10, 0, 0) = p₁ − p₀.
        Near(new Vector3(10f, 0f, 0f), CatmullRom.Derivative(points, 0, 0f), 1e-4f);
        // Mirrored at the far end: p₂ − p₁ = (0, 0, 10).
        Near(new Vector3(0f, 0f, 10f), CatmullRom.Derivative(points, 1, 1f), 1e-4f);
    }

    [Fact]
    public void TwoPointsRunInAStraightLineAtEvenSpeed()
    {
        Vector3[] points = [new(0f, 0f, 0f), new(10f, 0f, 0f)];

        // Reflected ends extend the line evenly, so both tangents are p₁ − p₀ = (10, 0, 0) and the Hermite curve is that
        // line at even speed: at t = 0.3, (3, 0, 0), heading (10, 0, 0) all the way.
        Near(new Vector3(3f, 0f, 0f), CatmullRom.Evaluate(points, 0, 0.3f), 1e-4f);
        Near(new Vector3(10f, 0f, 0f), CatmullRom.Derivative(points, 0, 0f), 1e-4f);
        Near(new Vector3(10f, 0f, 0f), CatmullRom.Derivative(points, 0, 0.5f), 1e-4f);
        Near(new Vector3(10f, 0f, 0f), CatmullRom.Derivative(points, 0, 1f), 1e-4f);
    }
}
