namespace Vista.Core.Tracks.Timing;

/// <summary>The cubic Hermite basis, shared by the timing curve and the aim channels.</summary>
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
}
