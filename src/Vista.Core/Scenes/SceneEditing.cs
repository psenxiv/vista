using Vista.Core.Editing;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's tracks: add, rename, duplicate, delete, reorder and hide.</summary>
public static class SceneEditing
{
    /// <summary>A scene holding one empty track, "Track 1".</summary>
    public static Scene New() => new([TrackEditing.Empty()], new HashSet<Guid>(), []);

    /// <summary>The index of track <paramref name="id"/>, or −1.</summary>
    public static int IndexOf(Scene scene, Guid id)
    {
        for (var i = 0; i < scene.Tracks.Count; i++)
            if (scene.Tracks[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>Track <paramref name="id"/>.</summary>
    public static Track Get(Scene scene, Guid id) => scene.Tracks[Require(scene, id)];

    /// <summary>Puts <paramref name="track"/> in place of the track with its Id.</summary>
    public static Scene Replace(Scene scene, Track track)
    {
        var index = Require(scene, track.Id);
        if (ReferenceEquals(scene.Tracks[index], track))
            return scene;
        var tracks = scene.Tracks.ToArray();
        tracks[index] = track;
        return scene with { Tracks = tracks };
    }

    /// <summary>Adds an empty track at the end, named "Track N" for the new count.</summary>
    public static (Scene Scene, Guid Added) Add(Scene scene)
    {
        var track = TrackEditing.Empty(name: $"Track {scene.Tracks.Count + 1}");
        return (scene with { Tracks = [.. scene.Tracks, track] }, track.Id);
    }

    /// <summary>Renames track <paramref name="id"/>; an empty name is refused.</summary>
    public static Scene Rename(Scene scene, Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A track needs a name.");
        name = name.Trim();
        var track = Get(scene, id);
        return track.Name == name ? scene : Replace(scene, track with { Name = name });
    }

    /// <summary>Inserts a copy of track <paramref name="id"/> after it, with a new Id and "copy" after its name.</summary>
    public static (Scene Scene, Guid Copy) Duplicate(Scene scene, Guid id)
    {
        var index = Require(scene, id);
        var original = scene.Tracks[index];
        var copy = original with { Id = Guid.NewGuid(), Name = $"{original.Name} copy" };
        var tracks = scene.Tracks.ToList();
        tracks.Insert(index + 1, copy);
        return (scene with { Tracks = tracks }, copy.Id);
    }

    /// <summary>Deletes tracks <paramref name="ids"/> and their playlist entries, refusing to delete every track; names the track to edit after: <paramref name="edited"/> if it stays, else the first remaining track after it, or the last.</summary>
    public static (Scene Scene, Guid Edited) Delete(Scene scene, IReadOnlyCollection<Guid> ids, Guid edited)
    {
        foreach (var id in ids)
            Require(scene, id);
        var at = Require(scene, edited);
        var gone = ids.ToHashSet();
        var tracks = scene.Tracks.Where(t => !gone.Contains(t.Id)).ToList();
        if (tracks.Count == 0)
            throw new ArgumentException("A scene keeps at least one track.");

        var next = gone.Contains(edited)
            ? scene.Tracks.Skip(at + 1).FirstOrDefault(t => !gone.Contains(t.Id)) ?? tracks[^1]
            : scene.Tracks[at];
        var hidden = new HashSet<Guid>(scene.Hidden.Where(id => !gone.Contains(id)));
        return (
            scene with
            {
                Tracks = tracks,
                Hidden = hidden,
                Playlist = scene.Playlist.Where(e => !gone.Contains(e.TrackId)).ToArray(),
            },
            next.Id
        );
    }

    /// <summary>Puts the tracks in <paramref name="order"/> (old indices).</summary>
    public static Scene Reorder(Scene scene, IReadOnlyList<int> order)
    {
        var tracks = BlockMove.Apply(scene.Tracks, order);
        return tracks.SequenceEqual(scene.Tracks) ? scene : scene with { Tracks = tracks };
    }

    /// <summary>Hides or shows tracks <paramref name="ids"/>.</summary>
    public static Scene SetHidden(Scene scene, IReadOnlyCollection<Guid> ids, bool hidden)
    {
        foreach (var id in ids)
            Require(scene, id);
        if (ids.All(id => scene.Hidden.Contains(id) == hidden))
            return scene;
        var set = new HashSet<Guid>(scene.Hidden);
        foreach (var id in ids)
        {
            if (hidden)
                set.Add(id);
            else
                set.Remove(id);
        }

        return scene with
        {
            Hidden = set,
        };
    }

    private static int Require(Scene scene, Guid id) =>
        IndexOf(scene, id) is var index and >= 0 ? index : throw new ArgumentException("There is no such track.");
}
