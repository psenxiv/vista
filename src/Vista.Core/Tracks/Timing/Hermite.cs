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
}
