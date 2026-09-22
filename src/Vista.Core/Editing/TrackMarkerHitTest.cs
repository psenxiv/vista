using System.Numerics;

namespace Vista.Core.Editing;

/// <summary>What a marker on screen stands for.</summary>
public enum MarkerKind { Point, TrackAnchor, SceneAnchor }

/// <summary>One marker on screen; <see cref="Screen"/> is null when off screen, and anchors use point −1.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen, MarkerKind Kind = MarkerKind.Point);

/// <summary>Finds which marker a click landed on when several tracks and their anchors are drawn.</summary>
public static class TrackMarkerHitTest
{
    /// <summary>The index of the hit marker, or null: edited points, the edited anchor, other points, other anchors, then the scene anchor.</summary>
    public static int? Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)
        => NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.SceneAnchor, laterWinsTie: false);

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
