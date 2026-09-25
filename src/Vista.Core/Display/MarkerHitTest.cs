using System.Numerics;

namespace Vista.Core.Display;

/// <summary>Finds which projected marker a click landed on.</summary>
public static class MarkerHitTest
{
    /// <summary>Index of the marker nearest <paramref name="cursor"/> within <paramref name="radius"/> pixels, or null; a tie goes to the later one. Null markers are off screen.</summary>
    public static int? Nearest(IReadOnlyList<Vector2?> markers, Vector2 cursor, float radius) =>
        Nearest(markers, m => m, cursor, radius, laterWinsTie: true);

    /// <summary>Index of the item nearest <paramref name="cursor"/> within <paramref name="radius"/> pixels, placed on screen by <paramref name="at"/> (null to skip it), or null; a tie goes to the later item when <paramref name="laterWinsTie"/>, else the earlier.</summary>
    public static int? Nearest<T>(
        IReadOnlyList<T> items,
        Func<T, Vector2?> at,
        Vector2 cursor,
        float radius,
        bool laterWinsTie
    )
    {
        int? best = null;
        var bestDistance = radius * radius;
        for (var i = 0; i < items.Count; i++)
        {
            if (at(items[i]) is not { } place)
                continue;
            var distance = Vector2.DistanceSquared(place, cursor);
            if (distance > bestDistance || (distance == bestDistance && best is not null && !laterWinsTie))
                continue;
            best = i;
            bestDistance = distance;
        }

        return best;
    }
}
