using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Where a scene's tracks sit in the world, where anchors start, and how moving them carries what hangs off them.</summary>
public static class SceneGeometry
{
    /// <summary>How far behind an anchor, along its yaw, the camera stops when flying to it.</summary>
    public const float ViewBack = 5f;

    /// <summary>How far above an anchor the camera stops when flying to it.</summary>
    public const float ViewUp = 3f;

    /// <summary>A track's anchor in the world.</summary>
    public static Anchor WorldAnchor(Scene scene, Track track) => scene.Anchor.ToWorld(track.Anchor);

    /// <summary>The track with its points in the world; the track itself when nothing moves it. Only the points are world values.</summary>
    public static Track InWorld(Scene scene, Track track)
    {
        var anchor = WorldAnchor(scene, track);
        if (anchor == Anchor.Origin || track.Points.Count == 0) return track;
        return track with { Points = track.Points.Select(anchor.ToWorld).ToArray() };
    }

    /// <summary>Places the scene's and the track's anchors under a first point at ground height, yaw 0, where not placed yet.</summary>
    public static Scene PlaceFor(Scene scene, Guid trackId, Vector3 worldPosition, float groundHeight)
    {
        var ground = new Anchor(worldPosition with { Y = groundHeight }, 0f);
        var result = scene.AnchorPlaced ? scene : scene with { Anchor = ground, AnchorPlaced = true };
        var track = SceneEditing.Get(result, trackId);
        if (track.AnchorPlaced) return result;
        return SceneEditing.Replace(result, track with { Anchor = result.Anchor.ToLocal(ground), AnchorPlaced = true });
    }

    /// <summary>Moves the scene anchor to <paramref name="to"/>, carrying every track, or alone so every point stays where it is.</summary>
    public static Scene MoveSceneAnchor(Scene scene, Anchor to, bool carry)
    {
        if (carry) return scene with { Anchor = to, AnchorPlaced = true };
        var tracks = scene.Tracks.Select(t => t with { Anchor = to.ToLocal(scene.Anchor.ToWorld(t.Anchor)) }).ToArray();
        return scene with { Tracks = tracks, Anchor = to, AnchorPlaced = true };
    }

    /// <summary>Moves a track's anchor to <paramref name="toWorld"/>, carrying its points, or alone so they stay where they are.</summary>
    public static Scene MoveTrackAnchor(Scene scene, Guid trackId, Anchor toWorld, bool carry)
    {
        var track = SceneEditing.Get(scene, trackId);
        var moved = track with { Anchor = scene.Anchor.ToLocal(toWorld), AnchorPlaced = true };
        if (!carry)
        {
            var from = WorldAnchor(scene, track);
            moved = moved with { Points = track.Points.Select(p => toWorld.ToLocal(from.ToWorld(p))).ToArray() };
        }

        return SceneEditing.Replace(scene, moved);
    }

    /// <summary>Where the camera goes to look at an anchor: behind it along its yaw and above it.</summary>
    public static (Vector3 Position, Vector3 LookAt) ViewOf(Anchor worldAnchor)
    {
        var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, worldAnchor.Yaw, 0f));
        return (worldAnchor.Position - (forward * ViewBack) + new Vector3(0f, ViewUp, 0f), worldAnchor.Position);
    }
}
