namespace Vista.Core.Display;

/// <summary>How fast a list scrolls while rows are dragged near its top or bottom edge.</summary>
public static class EdgeScroll
{
    /// <summary>The fastest it scrolls, in rows a second, with the mouse at the list's edge or past it.</summary>
    public const float MostRowsPerSecond = 10f;

    /// <summary>Rows a second to scroll, negative up, for the mouse at <paramref name="mouseY"/> over a list from <paramref name="top"/> to <paramref name="bottom"/>: within a row of either edge (a third of a short list), from 0 at the zone's inner side to <see cref="MostRowsPerSecond"/> at the edge and past it, and 0 between.</summary>
    public static float RowsPerSecond(float mouseY, float top, float bottom, float rowHeight)
    {
        var zone = MathF.Min(rowHeight, (bottom - top) / 3f);
        if (zone <= 0f)
            return 0f;
        if (mouseY < top + zone)
            return -MostRowsPerSecond * MathF.Min((top + zone - mouseY) / zone, 1f);
        if (mouseY > bottom - zone)
            return MostRowsPerSecond * MathF.Min((mouseY - (bottom - zone)) / zone, 1f);
        return 0f;
    }
}
