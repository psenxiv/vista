using Vista.Core.Editing;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's playlist: add, remove, reorder and loop counts, and what Live can play.</summary>
public static class PlaylistEditing
{
    /// <summary>The most times an entry can play its track.</summary>
    public const int MaxLoops = 99;

    /// <summary>Adds an entry for track <paramref name="trackId"/> at <paramref name="index"/>, or at the end.</summary>
    public static (Scene Scene, Guid Added) Add(Scene scene, Guid trackId, int? index = null)
    {
        SceneEditing.Get(scene, trackId);
        var entry = new PlaylistEntry(Guid.NewGuid(), trackId);
        var entries = scene.Playlist.ToList();
        entries.Insert(Math.Clamp(index ?? entries.Count, 0, entries.Count), entry);
        return (scene with { Playlist = entries }, entry.Id);
    }

    /// <summary>Removes entry <paramref name="entryId"/>.</summary>
    public static Scene Remove(Scene scene, Guid entryId)
    {
        var index = Require(scene, entryId);
        var entries = scene.Playlist.ToList();
        entries.RemoveAt(index);
        return scene with { Playlist = entries };
    }

    /// <summary>Puts the entries in <paramref name="order"/> (old indices).</summary>
    public static Scene Reorder(Scene scene, IReadOnlyList<int> order)
    {
        var entries = BlockMove.Apply(scene.Playlist, order);
        return entries.SequenceEqual(scene.Playlist) ? scene : scene with { Playlist = entries };
    }

    /// <summary>Sets how many times an entry plays, 1 to <see cref="MaxLoops"/>, or null to follow its track.</summary>
    public static Scene SetLoops(Scene scene, Guid entryId, int? loops)
    {
        var index = Require(scene, entryId);
        var clamped = loops is { } n ? Math.Clamp(n, 1, MaxLoops) : (int?)null;
        if (scene.Playlist[index].Loops == clamped) return scene;
        var entries = scene.Playlist.ToArray();
        entries[index] = entries[index] with { Loops = clamped };
        return scene with { Playlist = entries };
    }

    /// <summary>Sets whether Live wraps from the last entry to the first.</summary>
    public static Scene SetPlaylistLoops(Scene scene, bool loops) => scene.PlaylistLoops == loops ? scene : scene with { PlaylistLoops = loops };

    /// <summary>The index of entry <paramref name="entryId"/>, or −1.</summary>
    public static int IndexOf(Scene scene, Guid entryId)
    {
        for (var i = 0; i < scene.Playlist.Count; i++)
            if (scene.Playlist[i].Id == entryId) return i;
        return -1;
    }

    /// <summary>True when an entry holds the playlist for good: no loop count and a looping track with points.</summary>
    public static bool HoldsPlaylist(Scene scene, PlaylistEntry entry)
        => entry.Loops is null && SceneEditing.Get(scene, entry.TrackId) is { Loop: true, Points.Count: > 0 };

    /// <summary>True when an entry's track has points, so Live has something to play.</summary>
    public static bool CanPlay(Scene scene) => scene.Playlist.Any(e => SceneEditing.Get(scene, e.TrackId).Points.Count > 0);

    private static int Require(Scene scene, Guid entryId)
        => IndexOf(scene, entryId) is var index and >= 0 ? index : throw new ArgumentException("There is no such playlist entry.");
}
