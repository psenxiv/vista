using System.Numerics;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Moves points from one track onto the end of another.</summary>
public static class PointTransfer
{
    /// <summary>Moves source points <paramref name="points"/>, at world points <paramref name="world"/>, onto the end of <paramref name="destination"/> or a new track; names the destination and the moved points' indices there.</summary>
    public static (Scene Scene, Guid Destination, IReadOnlyList<int> Moved) Move(
        Scene scene, Guid source, IReadOnlyList<int> points, IReadOnlyList<ControlPoint> world,
        Guid? destination, Func<Vector3, float?> groundBelow)
    {
        var from = SceneEditing.Get(scene, source);
        if (points.Count == 0) throw new ArgumentException("There are no points to move.");
        if (world.Count != points.Count) throw new ArgumentException("Each moved point needs its place in the world.");
        if (points.Distinct().Count() != points.Count || points.Any(p => p < 0 || p >= from.Points.Count))
            throw new ArgumentException("There is no such point.");
        if (destination == source) throw new ArgumentException("The points are already on that track.");
        if (destination is { } named && SceneEditing.Get(scene, named).Aim == AimMode.FollowTarget)
            throw new ArgumentException("A Follow Target track has one point");

        var moving = points.Zip(world, (index, at) => (Index: index, World: at)).OrderBy(m => m.Index).ToArray();

        var left = from;
        for (var k = moving.Length - 1; k >= 0; k--) left = TrackEditing.Delete(left, moving[k].Index);
        var result = SceneEditing.Replace(scene, left);

        Guid to;
        if (destination is { } existing) to = existing;
        else (result, to) = SceneEditing.Add(result);

        var first = moving[0].World.Position;
        if (!(result.AnchorPlaced && SceneEditing.Get(result, to).AnchorPlaced))
            result = SceneGeometry.PlaceFor(result, to, first, groundBelow(first) ?? first.Y);

        var track = SceneEditing.Get(result, to);
        var anchor = SceneGeometry.WorldAnchor(result, track);
        var added = moving.Select(m => anchor.ToLocal(m.World));
        var timing = moving.Select((m, k) => from.Timing[m.Index] with
        {
            LegSpeed = k > 0 && moving[k - 1].Index == m.Index - 1 ? from.Timing[m.Index].LegSpeed : null,
        });

        var start = track.Points.Count;
        result = SceneEditing.Replace(result, track with { Points = [.. track.Points, .. added], Timing = [.. track.Timing, .. timing] });
        return (result, to, Enumerable.Range(start, moving.Length).ToArray());
    }
}
