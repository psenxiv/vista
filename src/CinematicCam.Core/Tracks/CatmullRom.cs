using System.Numerics;

namespace CinematicCam.Core;

/// <summary>Centripetal Catmull-Rom spline through a track's control points. The path is always open.</summary>
public static class CatmullRom
{
    /// <summary>Centripetal parameterisation exponent.</summary>
    public const float Alpha = 0.5f;

    /// <summary>Below this, a knot interval is treated as zero rather than divided by.</summary>
    private const float KnotEpsilon = 1e-6f;

    /// <summary>A point's value and its derivative with respect to the spline parameter u.</summary>
    private readonly record struct Dual(Vector3 Value, Vector3 Deriv);

    /// <summary>Number of curve segments for a point count.</summary>
    public static int SegmentCount(int pointCount) => Math.Max(pointCount - 1, 0);

    /// <summary>Position on the curve at parameter t in [0, 1] across the given segment.</summary>
    public static Vector3 Evaluate(IReadOnlyList<Vector3> points, int segment, float t)
        => EvaluateCore(points, segment, t).Value;

    /// <summary>d/dt of the curve at parameter t in [0, 1] across the given segment.</summary>
    public static Vector3 Derivative(IReadOnlyList<Vector3> points, int segment, float t)
        => EvaluateCore(points, segment, t).Deriv;

    private static Dual EvaluateCore(IReadOnlyList<Vector3> points, int segment, float t)
    {
        ValidateSegment(points.Count, segment);

        var p0 = GetPoint(points, segment - 1);
        var p1 = GetPoint(points, segment);
        var p2 = GetPoint(points, segment + 1);
        var p3 = GetPoint(points, segment + 2);

        var t0 = 0f;
        var t1 = t0 + KnotDelta(p0, p1);
        var t2 = t1 + KnotDelta(p1, p2);
        var t3 = t2 + KnotDelta(p2, p3);
        var u = t1 + (t * (t2 - t1));

        var leaf0 = new Dual(p0, Vector3.Zero);
        var leaf1 = new Dual(p1, Vector3.Zero);
        var leaf2 = new Dual(p2, Vector3.Zero);
        var leaf3 = new Dual(p3, Vector3.Zero);

        var a1 = Lerp(leaf0, leaf1, t0, t1, u);
        var a2 = Lerp(leaf1, leaf2, t1, t2, u);
        var a3 = Lerp(leaf2, leaf3, t2, t3, u);
        var b1 = Lerp(a1, a2, t0, t2, u);
        var b2 = Lerp(a2, a3, t1, t3, u);
        var c = Lerp(b1, b2, t1, t2, u);

        // u is affine in t (u = t1 + t * (t2 - t1)), so scale the u-derivative by du/dt.
        return c with { Deriv = c.Deriv * (t2 - t1) };
    }

    /// <summary>Barry-Goldman linear step, clamped to the start point on a zero knot interval.</summary>
    private static Dual Lerp(Dual a, Dual b, float ta, float tb, float u)
    {
        var denom = tb - ta;
        if (MathF.Abs(denom) < KnotEpsilon) return a;

        var factor = (u - ta) / denom;
        var value = a.Value + ((b.Value - a.Value) * factor);
        var deriv = a.Deriv + ((b.Deriv - a.Deriv) * factor) + ((b.Value - a.Value) * (1f / denom));
        return new Dual(value, deriv);
    }

    private static float KnotDelta(Vector3 a, Vector3 b) => MathF.Pow(Vector3.Distance(a, b), Alpha);

    /// <summary>Endpoints duplicate to supply the phantom points.</summary>
    private static Vector3 GetPoint(IReadOnlyList<Vector3> points, int index)
        => points[Math.Clamp(index, 0, points.Count - 1)];

    private static void ValidateSegment(int pointCount, int segment)
    {
        var count = SegmentCount(pointCount);
        if (segment < 0 || segment >= count)
            throw new ArgumentOutOfRangeException(nameof(segment), segment, $"segment must be in [0, {count}) for {pointCount} point(s)");
    }
}
