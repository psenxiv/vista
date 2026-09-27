using System.Numerics;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks.Spline;

/// <summary>Centripetal Catmull-Rom spline through a track's control points, evaluated in Hermite form. The path is always open.</summary>
public static class CatmullRom
{
    /// <summary>Below this, a knot interval is treated as zero rather than divided by.</summary>
    private const float KnotEpsilon = 1e-6f;

    /// <summary>Number of curve segments for a point count.</summary>
    public static int SegmentCount(int pointCount) => Math.Max(pointCount - 1, 0);

    /// <summary>Position on the curve at parameter t in [0, 1] across the given segment.</summary>
    public static Vector3 Evaluate(IReadOnlyList<Vector3> points, int segment, float t)
    {
        var (p1, p2, m1, m2) = Segment(points, segment);
        return Hermite.At(p1, p2, m1, m2, t);
    }

    /// <summary>d/dt of the curve at parameter t in [0, 1] across the given segment.</summary>
    public static Vector3 Derivative(IReadOnlyList<Vector3> points, int segment, float t)
    {
        var (p1, p2, m1, m2) = Segment(points, segment);
        return Hermite.Slope(p1, p2, m1, m2, t);
    }

    /// <summary>A segment's two end points and their tangents in the segment's own parameter (Yuksel et al.'s centripetal tangents); a segment of coincident points is that point, standing still.</summary>
    private static (Vector3 P1, Vector3 P2, Vector3 M1, Vector3 M2) Segment(IReadOnlyList<Vector3> points, int segment)
    {
        ValidateSegment(points.Count, segment);
        var p0 = GetPoint(points, segment - 1);
        var p1 = GetPoint(points, segment);
        var p2 = GetPoint(points, segment + 1);
        var p3 = GetPoint(points, segment + 2);
        var d0 = KnotDelta(p0, p1);
        var d1 = KnotDelta(p1, p2);
        var d2 = KnotDelta(p2, p3);
        if (d1 < KnotEpsilon)
            return (p1, p1, Vector3.Zero, Vector3.Zero);
        return (p1, p2, d1 * Rate(p0, p1, p2, d0, d1), d1 * Rate(p1, p2, p3, d1, d2));
    }

    /// <summary>The curve's rate per knot at <paramref name="b"/>, between <paramref name="a"/> and <paramref name="c"/> with knot intervals <paramref name="before"/> and <paramref name="after"/>; a term over a zero interval is left out.</summary>
    private static Vector3 Rate(Vector3 a, Vector3 b, Vector3 c, float before, float after)
    {
        var rate = -(c - a) / (before + after);
        if (before >= KnotEpsilon)
            rate += (b - a) / before;
        if (after >= KnotEpsilon)
            rate += (c - b) / after;
        return rate;
    }

    /// <summary>Centripetal knot interval: the square root of the distance.</summary>
    private static float KnotDelta(Vector3 a, Vector3 b) => MathF.Sqrt(Vector3.Distance(a, b));

    /// <summary>The point at <paramref name="index"/>, or just past either end the end point reflected through its neighbour.</summary>
    private static Vector3 GetPoint(IReadOnlyList<Vector3> points, int index)
    {
        var last = points.Count - 1;
        return index < 0 ? (2f * points[0]) - points[1]
            : index > last ? (2f * points[last]) - points[last - 1]
            : points[index];
    }

    private static void ValidateSegment(int pointCount, int segment)
    {
        var count = SegmentCount(pointCount);
        if (segment < 0 || segment >= count)
            throw new ArgumentOutOfRangeException(
                nameof(segment),
                segment,
                $"segment must be in [0, {count}) for {pointCount} point(s)"
            );
    }
}
