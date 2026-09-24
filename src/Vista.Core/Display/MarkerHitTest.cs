using System.Numerics;

namespace Vista.Core.Display;

/// <summary>Finds which projected marker a click landed on.</summary>
public static class MarkerHitTest
{
    /// <summary>Index of the marker nearest <paramref name="cursor"/> within <paramref name="radius"/> pixels, or null. Null markers are off screen.</summary>
    public static int? Nearest(IReadOnlyList<Vector2?> markers, Vector2 cursor, float radius)
    {
        int? best = null;
        var bestDistance = radius * radius;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i] is not { } marker) continue;
            var distance = Vector2.DistanceSquared(marker, cursor);
            if (distance > bestDistance) continue;
            best = i;
            bestDistance = distance;
        }

        return best;
    }
}
