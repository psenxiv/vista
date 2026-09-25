namespace Vista.Core.Tracks;

/// <summary>Fractions of the way along a span: clamped to [0, 1], and where a value falls between two ends.</summary>
public static class Fraction
{
    /// <summary><paramref name="value"/> clamped to [0, 1].</summary>
    public static float Clamp(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>How far <paramref name="value"/> is from <paramref name="from"/> to <paramref name="to"/>, clamped to [0, 1], or <paramref name="empty"/> when <paramref name="to"/> isn't above <paramref name="from"/>.</summary>
    public static float Between(float value, float from, float to, float empty)
    {
        var span = to - from;
        return span > 0f ? Clamp((value - from) / span) : empty;
    }
}
