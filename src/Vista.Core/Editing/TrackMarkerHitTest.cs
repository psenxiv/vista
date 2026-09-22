using System.Numerics;

namespace Vista.Core.Editing;

/// <summary>One point's marker on screen; <see cref="Screen"/> is null when off screen.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen);

/// <summary>Finds which marker a click landed on when several tracks are drawn.</summary>
public static class TrackMarkerHitTest
{
    /// <summary>The index of the hit marker, or null: the edited track's nearest within <paramref name="radius"/>, else the nearest other, later winning a tie.</summary>
    public static int? Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)
        => NearestOf(markers, cursor, radius, m => m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Track != edited, laterWinsTie: true);

    private static int? NearestOf(IReadOnlyList<TrackMarker> markers, Vector2 cursor, float radius, Func<TrackMarker, bool> include, bool laterWinsTie)
    {
        int? best = null;
        var bestDistance = radius * radius;
        for (var i = 0; i < markers.Count; i++)
        {
            if (!include(markers[i]) || markers[i].Screen is not { } at) continue;
            var distance = Vector2.DistanceSquared(at, cursor);
            if (distance > bestDistance || (distance == bestDistance && best is not null && !laterWinsTie)) continue;
            best = i;
            bestDistance = distance;
        }

        return best;
    }
}
