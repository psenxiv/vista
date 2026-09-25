namespace Vista.Core.Tracks.Timing;

/// <summary>The cubic Hermite basis, shared by the timing curve, the aim channels and eased turns.</summary>
internal static class Hermite
{
    /// <summary>The curve from <paramref name="p0"/> to <paramref name="p1"/> with tangents <paramref name="m0"/> and <paramref name="m1"/>, at <paramref name="t"/> in [0, 1].</summary>
    public static float At(float p0, float p1, float m0, float m1, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = (2f * t3) - (3f * t2) + 1f;
        var h10 = t3 - (2f * t2) + t;
        var h01 = (-2f * t3) + (3f * t2);
        var h11 = t3 - t2;
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary><see cref="At"/>'s rate of change with <paramref name="t"/>, over the unit span.</summary>
    public static float Slope(float p0, float p1, float m0, float m1, float t)
    {
        var t2 = t * t;
        return (((6f * t2) - (6f * t)) * p0)
            + (((3f * t2) - (4f * t) + 1f) * m0)
            + (((-6f * t2) + (6f * t)) * p1)
            + (((3f * t2) - (2f * t)) * m1);
    }

    /// <summary>0 to 1 over <paramref name="t"/> in [0, 1], starting and stopping gently: the curve between flat ends.</summary>
    public static float Smoothstep(float t) => t * t * (3f - (2f * t));
}
