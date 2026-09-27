namespace Vista.Core.Display;

/// <summary>The size range of the track names drawn above track anchors, as a multiple of the font size.</summary>
public static class TrackNameSize
{
    public const float Min = 0.75f;

    public const float Max = 3f;

    /// <summary>The size names start at.</summary>
    public const float Default = 1.5f;

    /// <summary><paramref name="size"/> within range; a value that isn't finite gives <see cref="Default"/>.</summary>
    public static float Clamp(float size) => float.IsFinite(size) ? Math.Clamp(size, Min, Max) : Default;
}
