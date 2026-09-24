using System.Numerics;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Scenes;

/// <summary>Where a scene's tracks sit in the world, where anchors start, and how moving them carries what hangs off them.</summary>
public static class SceneGeometry
{
    /// <summary>A track's anchor in the world.</summary>
    public static Anchor WorldAnchor(Scene scene, Track track) => scene.Anchor.ToWorld(track.Anchor);

    /// <summary>The track with its anchor, points and Look At point in the world; the track itself when nothing moves it.</summary>
    public static Track InWorld(Scene scene, Track track)
    {
        var anchor = WorldAnchor(scene, track);
        if (anchor == Anchor.Origin && track.Anchor == Anchor.Origin) return track;
        return track with { Anchor = anchor, Points = track.Points.Select(anchor.ToWorld).ToArray(), LookAt = anchor.ToWorld(track.LookAt) };
    }

    /// <summary>Places the scene's and the track's anchors under a first point at ground height, yaw 0, where not placed yet; a Look At point already placed stays in the world.</summary>
    public static Scene PlaceFor(Scene scene, Guid trackId, Vector3 worldPosition, float groundHeight)
    {
        var ground = new Anchor(worldPosition with { Y = groundHeight }, 0f);
        var result = PlaceScene(scene, ground.Position);
        var track = SceneEditing.Get(result, trackId);
        if (track.AnchorPlaced) return result;
        var anchored = track with { Anchor = result.Anchor.ToLocal(ground), AnchorPlaced = true };
        return SceneEditing.Replace(result, KeepLookAt(anchored, WorldAnchor(result, track), WorldAnchor(result, anchored)));
    }

    /// <summary>Places an unplaced scene anchor at <paramref name="ground"/>, yaw 0, a Look At point already placed staying in the world; a placed scene as it is.</summary>
    public static Scene PlaceScene(Scene scene, Vector3 ground)
    {
        if (scene.AnchorPlaced) return scene;
        var placed = scene with { Anchor = new Anchor(ground, 0f), AnchorPlaced = true };
        return placed with { Tracks = scene.Tracks.Select(t => KeepLookAt(t, WorldAnchor(scene, t), WorldAnchor(placed, t))).ToArray() };
    }

    /// <summary>Moves the scene anchor to <paramref name="to"/>, carrying every track, or alone so every point stays where it is.</summary>
    public static Scene MoveSceneAnchor(Scene scene, Anchor to, bool carry)
    {
        if (carry) return scene with { Anchor = to, AnchorPlaced = true };
        var tracks = scene.Tracks.Select(t => t with { Anchor = to.ToLocal(scene.Anchor.ToWorld(t.Anchor)) }).ToArray();
        return scene with { Tracks = tracks, Anchor = to, AnchorPlaced = true };
    }

    /// <summary>Moves a track's anchor to <paramref name="toWorld"/>, carrying its points and Look At point, or alone so they stay where they are; a Follow Target track's point is an offset from its character and stays as it is.</summary>
    public static Scene MoveTrackAnchor(Scene scene, Guid trackId, Anchor toWorld, bool carry)
    {
        var track = SceneEditing.Get(scene, trackId);
        var moved = track with { Anchor = scene.Anchor.ToLocal(toWorld), AnchorPlaced = true };
        if (!carry)
        {
            var from = WorldAnchor(scene, track);
            if (track.Aim != AimMode.FollowTarget) moved = moved with { Points = track.Points.Select(p => toWorld.ToLocal(from.ToWorld(p))).ToArray() };
            moved = KeepLookAt(moved, from, toWorld);
        }

        return SceneEditing.Replace(scene, moved);
    }

    /// <summary>The track with its placed Look At point re-expressed so it stays in the world when its anchor goes from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Track KeepLookAt(Track track, Anchor from, Anchor to)
        => track.LookAtPlaced ? track with { LookAt = to.ToLocal(from.ToWorld(track.LookAt)) } : track;
}
