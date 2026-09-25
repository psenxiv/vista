using System.Globalization;
using Vista.Core.Editing;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's playlist: add, remove, reorder and loop counts, and what Live can play.</summary>
public static class PlaylistEditing
{
    /// <summary>The most times an entry can play its track.</summary>
    public const int MaxLoops = 99;

    /// <summary>Why a playlist entry Id can't be used: no entry has it.</summary>
    public const string NoSuchEntry = "There is no such playlist entry.";

    /// <summary>Adds an entry for each of <paramref name="trackIds"/>, in that order, at <paramref name="index"/>, or at the end; an index past either end of the playlist adds at that end rather than being refused.</summary>
    public static Scene Add(Scene scene, IReadOnlyList<Guid> trackIds, int? index = null)
    {
        SceneEditing.RequireAll(scene, trackIds);
        if (trackIds.Count == 0)
            return scene;
        var count = scene.Playlist.Count;
        return scene with
        {
            Playlist = ListEdit.InsertRange(
                scene.Playlist,
                Math.Clamp(index ?? count, 0, count),
                trackIds.Select(id => new PlaylistEntry(Guid.NewGuid(), id))
            ),
        };
    }

    /// <summary>Removes entries <paramref name="entryIds"/>.</summary>
    public static Scene Remove(Scene scene, IReadOnlyCollection<Guid> entryIds)
    {
        RequireAll(scene, entryIds);
        if (entryIds.Count == 0)
            return scene;
        return scene with { Playlist = scene.Playlist.Where(e => !entryIds.Contains(e.Id)).ToArray() };
    }

    /// <summary>Puts the entries in <paramref name="order"/> (old indices).</summary>
    public static Scene Reorder(Scene scene, IReadOnlyList<int> order)
    {
        var entries = BlockMove.Apply(scene.Playlist, order);
        return entries.SequenceEqual(scene.Playlist) ? scene : scene with { Playlist = entries };
    }

    /// <summary>Reads a typed repeat count: blank, 0 or less gives null (follow the track), above <see cref="MaxLoops"/> gives it; false when <paramref name="text"/> isn't a whole number.</summary>
    public static bool ParseLoops(string text, out int? loops)
    {
        loops = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return true;
        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var count))
            return false;
        loops = count <= 0 ? null : (int)Math.Min(count, MaxLoops);
        return true;
    }

    /// <summary>Sets how many times an entry plays, 1 to <see cref="MaxLoops"/>, or null to follow its track.</summary>
    public static Scene SetLoops(Scene scene, Guid entryId, int? loops)
    {
        var index = Require(scene, entryId);
        var clamped = loops is { } n ? Math.Clamp(n, 1, MaxLoops) : (int?)null;
        if (scene.Playlist[index].Loops == clamped)
            return scene;
        return scene with
        {
            Playlist = ListEdit.Replace(scene.Playlist, index, scene.Playlist[index] with { Loops = clamped }),
        };
    }

    /// <summary>How repeat count <paramref name="loops"/> reads; 0 follows the track: forever when that holds the playlist, else once.</summary>
    public static Repeats RepeatsOf(int loops, bool holds) =>
        loops > 0 ? Repeats.Count
        : holds ? Repeats.Forever
        : Repeats.Once;

    /// <summary>A repeat count one wheel notch on: up adds one to <see cref="MaxLoops"/>, down from 1 follows the track.</summary>
    public static int? StepLoops(int? loops, bool up) =>
        up ? Math.Min((loops ?? 0) + 1, MaxLoops)
        : loops is { } n && n > 1 ? n - 1
        : null;

    /// <summary>Sets whether Live wraps from the last entry to the first.</summary>
    public static Scene SetPlaylistLoops(Scene scene, bool loops) =>
        scene.PlaylistLoops == loops ? scene : scene with { PlaylistLoops = loops };

    /// <summary>The index of entry <paramref name="entryId"/>, or −1.</summary>
    public static int IndexOf(Scene scene, Guid entryId) => ListEdit.IndexOf(scene.Playlist, e => e.Id == entryId);

    /// <summary>True when an entry holds the playlist for good: no loop count and a looping track with points.</summary>
    public static bool HoldsPlaylist(Scene scene, PlaylistEntry entry) =>
        entry.Loops is null && SceneEditing.Get(scene, entry.TrackId) is { Loop: true, Points.Count: > 0 };

    /// <summary>True when an entry's track has points, so Live has something to play.</summary>
    public static bool CanPlay(Scene scene) =>
        scene.Playlist.Any(e => SceneEditing.Get(scene, e.TrackId).Points.Count > 0);

    /// <summary>The index of entry <paramref name="entryId"/>, refusing an unknown one.</summary>
    public static int Require(Scene scene, Guid entryId) =>
        IndexOf(scene, entryId) is var index and >= 0 ? index : throw new ArgumentException(NoSuchEntry);

    /// <summary>Refuses unless every one of entries <paramref name="entryIds"/> is in the playlist.</summary>
    public static void RequireAll(Scene scene, IEnumerable<Guid> entryIds)
    {
        foreach (var id in entryIds)
            Require(scene, id);
    }
}
