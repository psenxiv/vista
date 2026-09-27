using System.Numerics;

namespace Vista.Core.Tracks.Timing;

/// <summary>The cubic Hermite basis, shared by the timing curve, the aim channels, eased turns and the path spline.</summary>
internal static class Hermite
{
    /// <summary>The curve from <paramref name="p0"/> to <paramref name="p1"/> with tangents <paramref name="m0"/> and <paramref name="m1"/>, at <paramref name="t"/> in [0, 1].</summary>
    public static float At(float p0, float p1, float m0, float m1, float t)
    {
        var (h00, h10, h01, h11) = Weights(t);
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary><see cref="At(float, float, float, float, float)"/> for points and tangents in space.</summary>
    public static Vector3 At(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
    {
        var (h00, h10, h01, h11) = Weights(t);
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary><see cref="At(float, float, float, float, float)"/>'s rate of change with <paramref name="t"/>, over the unit span.</summary>
    public static float Slope(float p0, float p1, float m0, float m1, float t)
    {
        var (h00, h10, h01, h11) = SlopeWeights(t);
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary><see cref="Slope(float, float, float, float, float)"/> for points and tangents in space.</summary>
    public static Vector3 Slope(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
    {
        var (h00, h10, h01, h11) = SlopeWeights(t);
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary>The Hermite basis at <paramref name="t"/>: the weights of the start, start tangent, end and end tangent.</summary>
    private static (float H00, float H10, float H01, float H11) Weights(float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return ((2f * t3) - (3f * t2) + 1f, t3 - (2f * t2) + t, (-2f * t3) + (3f * t2), t3 - t2);
    }

    /// <summary>The Hermite basis's rate of change at <paramref name="t"/>, in <see cref="Weights"/>' order.</summary>
    private static (float H00, float H10, float H01, float H11) SlopeWeights(float t)
    {
        var t2 = t * t;
        return ((6f * t2) - (6f * t), (3f * t2) - (4f * t) + 1f, (-6f * t2) + (6f * t), (3f * t2) - (2f * t));
    }

    /// <summary>0 to 1 over <paramref name="t"/> in [0, 1], starting and stopping gently: the curve between flat ends.</summary>
    public static float Smoothstep(float t) => t * t * (3f - (2f * t));

    /// <summary><paramref name="ratio"/>, a tangent over its interval's secant, clamped to [0, 3] so the cubic stays monotone (Fritsch–Carlson); infinity clamps to 3.</summary>
    public static float MonotoneRatio(float ratio) => Math.Clamp(ratio, 0f, MonotoneBound);

    /// <summary>The Fritsch–Carlson bound on a tangent-to-secant ratio.</summary>
    private const float MonotoneBound = 3f;

    /// <summary>Weights on the changes over the interval before a point and the one after, <paramref name="before"/> and <paramref name="after"/> long, whose sum is the three-point (Catmull-Rom) slope there.</summary>
    public static (float Before, float After) ThroughWeights(float before, float after) =>
        (after / (before * (before + after)), before / (after * (before + after)));

    /// <summary>Weights on the changes over an end interval <paramref name="end"/> long and the next, <paramref name="next"/> long, whose sum is the three-point end slope (SciPy PCHIP's end rule, unclamped).</summary>
    public static (float End, float Next) EndWeights(float end, float next) =>
        (((2f * end) + next) / (end * (end + next)), -end / (next * (end + next)));

    /// <summary>Key <paramref name="i"/>'s slope from the <paramref name="changes"/> over the intervals between <paramref name="keys"/> (interval k runs from key k − 1 to key k): the three-point slope, 0 where the intervals either side change opposite ways or not at all and otherwise at most <see cref="MonotoneBound"/> times the gentler one's rate; at an end, the end interval's own rate.</summary>
    public static float KeySlope(IReadOnlyList<float> keys, IReadOnlyList<float> changes, int i)
    {
        var last = keys.Count - 1;
        float Rate(int interval) => changes[interval] / (keys[interval] - keys[interval - 1]);
        if (i == 0 || i == last)
            return Rate(Math.Max(i, 1));

        var (before, after) = (Rate(i), Rate(i + 1));
        if (before * after <= 0f)
            return 0f;
        var gentler = MathF.Abs(before) < MathF.Abs(after) ? before : after;
        return MonotoneRatio(ThroughSlope(keys, changes, i) / gentler) * gentler;
    }

    /// <summary><see cref="KeySlope(IReadOnlyList{float}, IReadOnlyList{float}, int)"/> for turns as rotation vectors: a size of at most <see cref="MonotoneBound"/> times the gentler turn rate times the cosine between the two turns' axes, so 0 from right angles to opposite.</summary>
    public static Vector3 KeySlope(IReadOnlyList<float> keys, IReadOnlyList<Vector3> changes, int i)
    {
        var last = keys.Count - 1;
        Vector3 Rate(int interval) => changes[interval] / (keys[interval] - keys[interval - 1]);
        if (i == 0 || i == last)
            return Rate(Math.Max(i, 1));

        var (before, after) = (Rate(i), Rate(i + 1));
        // min(|b|, |a|)·cos θ as b·a / max(|b|, |a|): a turn too small to square (its size underflows to 0) can't make it NaN.
        var bound = Vector3.Dot(before, after) / MathF.Max(before.Length(), after.Length());
        if (!(bound > 0f))
            return Vector3.Zero;
        var raw = ThroughSlope(keys, changes, i);
        var size = raw.Length();
        return size == 0f ? raw : raw * (MonotoneRatio(size / bound) * bound / size);
    }

    /// <summary>The three-point slope through interior key <paramref name="i"/>, from <see cref="ThroughWeights"/>.</summary>
    private static float ThroughSlope(IReadOnlyList<float> keys, IReadOnlyList<float> changes, int i)
    {
        var (before, after) = ThroughWeights(keys[i] - keys[i - 1], keys[i + 1] - keys[i]);
        return (before * changes[i]) + (after * changes[i + 1]);
    }

    /// <summary><see cref="ThroughSlope(IReadOnlyList{float}, IReadOnlyList{float}, int)"/> for turns as rotation vectors.</summary>
    private static Vector3 ThroughSlope(IReadOnlyList<float> keys, IReadOnlyList<Vector3> changes, int i)
    {
        var (before, after) = ThroughWeights(keys[i] - keys[i - 1], keys[i + 1] - keys[i]);
        return (before * changes[i]) + (after * changes[i + 1]);
    }
}
