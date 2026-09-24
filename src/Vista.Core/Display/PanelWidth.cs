namespace Vista.Core.Display;

/// <summary>The width range of the Vista window's Hierarchy and Playlist panels, in pixels.</summary>
public static class PanelWidth
{
    public const float Min = 240f;

    public const float Max = 600f;

    /// <summary>The width a panel starts at.</summary>
    public const float Default = Min;

    /// <summary><paramref name="width"/> within range; not a number gives <see cref="Default"/>.</summary>
    public static float Clamp(float width) => float.IsNaN(width) ? Default : Math.Clamp(width, Min, Max);
}
