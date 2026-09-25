using System.Numerics;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Takes a track out of a scene as a preset and places a preset in a scene.</summary>
public static class Presets
{
    /// <summary>True when a track has points to save as a preset.</summary>
    public static bool CanSave(Track track) => track.Points.Count > 0;

    /// <summary>Track <paramref name="trackId"/> with its anchor at the origin, and that anchor's world yaw.</summary>
    public static Preset From(Scene scene, Guid trackId)
    {
        var track = SceneEditing.Get(scene, trackId);
        return new Preset(track with { Anchor = Anchor.Origin }, SceneGeometry.WorldAnchor(scene, track).Yaw);
    }

    /// <summary>Appends the preset as a new track named after it, numbered if the name is taken, its anchor at <paramref name="ground"/> with the preset's yaw, placing an unplaced scene anchor there first; a name too long is refused.</summary>
    public static (Scene Scene, Guid Added) Place(Scene scene, Preset preset, Vector3 ground)
    {
        var name = SceneNames.Numbered(preset.Track.Name, SceneEditing.Names(scene));
        if (name.Length > SceneNames.MaxLength)
            throw new ArgumentException("The track's name would be too long. Shorten the preset's name first.");
        var placed = SceneGeometry.PlaceScene(scene, ground);
        var track = preset.Track with
        {
            Id = Guid.NewGuid(),
            Name = name,
            Anchor = placed.Anchor.ToLocal(new Anchor(ground, preset.Yaw)),
            AnchorPlaced = true,
        };
        return (placed with { Tracks = [.. placed.Tracks, track] }, track.Id);
    }
}
