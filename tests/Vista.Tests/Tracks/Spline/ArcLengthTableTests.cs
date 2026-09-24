using System.Linq;
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class ArcLengthTableTests
{
    // Bunched-then-spread: points 0-1 are nearly coincident, then 2 and 3 spread out
    // further and further. Segment 1 (point 1 to point 2) is the long segment: its
    // neighbours' tangents make equal steps of spline parameter t crawl near point 1
    // and lurch near point 2.
    private static readonly Vector3[] BunchedThenSpread =
    {
        new(0, 0, 0),
        new(0.001f, 0, 0),
        new(10, 0, 0),
        new(1000, 0, 0),
    };

    [Fact]
    public void ArcLengthEvaluationGivesEvenSpacingOnALongSegment()
    {
        var table = new ArcLengthTable(BunchedThenSpread);
        const int segment = 1;
        const int steps = 5;

        var byArcLength = new Vector3[steps + 1];
        var byNaiveParameter = new Vector3[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var fraction = (float)i / steps;
            var t = table.ParameterAt(segment, fraction);
            byArcLength[i] = CatmullRom.Evaluate(BunchedThenSpread, segment, t);
            byNaiveParameter[i] = CatmullRom.Evaluate(BunchedThenSpread, segment, fraction);
        }

        var arcDistances = ConsecutiveDistances(byArcLength);
        var naiveDistances = ConsecutiveDistances(byNaiveParameter);

        var arcMean = arcDistances.Average();
        foreach (var d in arcDistances)
            Assert.True(MathF.Abs(d - arcMean) < arcMean * 0.05f, $"arc-length step {d} strays from mean {arcMean}");

        var naiveMean = naiveDistances.Average();
        Assert.True(naiveDistances.Max() > naiveMean * 1.5f,
            "naive parameter spacing should be uneven on a bunched-then-spread segment");
    }

    private static float[] ConsecutiveDistances(Vector3[] points)
    {
        var distances = new float[points.Length - 1];
        for (var i = 0; i < distances.Length; i++)
            distances[i] = Vector3.Distance(points[i], points[i + 1]);
        return distances;
    }

    [Fact]
    public void SegmentLengthAndTotalLengthAreConsistent()
    {
        var table = new ArcLengthTable(BunchedThenSpread);
        var sum = 0f;
        for (var i = 0; i < table.SegmentCount; i++)
            sum += table.SegmentLength(i);

        Assert.Equal(sum, table.TotalLength, 3);
    }

    [Fact]
    public void ParameterAtZeroAndOneReturnTheSegmentEndpoints()
    {
        var table = new ArcLengthTable(BunchedThenSpread);
        Assert.Equal(0f, table.ParameterAt(1, 0f), 3);
        Assert.Equal(1f, table.ParameterAt(1, 1f), 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DegenerateZeroOrOnePointsDoNotThrowAndHaveNoLength(int pointCount)
    {
        var points = new Vector3[pointCount];
        var table = new ArcLengthTable(points);
        Assert.Equal(0, table.SegmentCount);
        Assert.Equal(0f, table.TotalLength);
    }

    [Fact]
    public void TwoCoincidentPointsGiveAZeroLengthSegmentAndReturnFractionUnchanged()
    {
        var p = new Vector3(2, 2, 2);
        var points = new[] { p, p };

        var table = new ArcLengthTable(points);
        Assert.Equal(0f, table.SegmentLength(0));

        Assert.Equal(0.37f, table.ParameterAt(0, 0.37f));
        Assert.Equal(0f, table.ParameterAt(0, 0f));
        Assert.Equal(1f, table.ParameterAt(0, 1f));
    }

    [Fact]
    public void SegmentQueriesRejectAnOutOfRangeSegment()
    {
        var table = new ArcLengthTable(BunchedThenSpread);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SegmentLength(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SegmentLength(table.SegmentCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ParameterAt(-1, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ParameterAt(table.SegmentCount, 0.5f));
    }

    [Fact]
    public void BuildingTheTableForCoincidentPointsDoesNotThrowOrProduceNaN()
    {
        var p = new Vector3(-1, 4, 2);
        var points = new[] { p, p, p, p };

        var table = new ArcLengthTable(points);
        for (var segment = 0; segment < table.SegmentCount; segment++)
        {
            Assert.False(float.IsNaN(table.SegmentLength(segment)));
            Assert.False(float.IsNaN(table.ParameterAt(segment, 0.5f)));
        }
    }
}
