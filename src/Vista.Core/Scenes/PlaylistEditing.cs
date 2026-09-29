using System.Globalization;
using Vista.Core.Editing;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's playlists: add, remove, reorder and loop counts, and what Live can play.</summary>
public static class PlaylistEditing
{
    /// <summary>The name of a new scene's playlist.</summary>
    public const string FirstName = "Playlist 1";

    /// <summary>The most times an entry can play its track.</summary>
    public const int MaxLoops = 99;

    /// <summary>Why a playlist entry Id can't be used: no entry has it.</summary>
    public const string NoSuchEntry = "There is no such playlist entry.";

    /// <summary>An empty playlist named <see cref="FirstName"/>, with a new id.</summary>
    public static Playlist Empty() => new(Guid.NewGuid(), FirstName, []);

    /// <summary>The selected playlist, which Live plays and the Playlist panel shows.</summary>
    public static Playlist Selected(Scene scene) => scene.Playlists.First(p => p.Id == scene.SelectedPlaylistId);

    /// <summary>Adds an entry to the selected playlist for each of <paramref name="trackIds"/>, in that order, at <paramref name="index"/>, or at the end; an index past either end of the playlist adds at that end rather than being refused.</summary>
    public static Scene Add(Scene scene, IReadOnlyList<Guid> trackIds, int? index = null)
    {
        SceneEditing.RequireAll(scene, trackIds);
        if (trackIds.Count == 0)
            return scene;
        var playlist = Selected(scene);
        var count = playlist.Entries.Count;
        return WithPlaylist(
            scene,
            playlist with
            {
                Entries = ListEdit.InsertRange(
                    playlist.Entries,
                    Math.Clamp(index ?? count, 0, count),
                    trackIds.Select(id => new PlaylistEntry(Guid.NewGuid(), id))
                ),
            }
        );
    }

    /// <summary>Removes entries <paramref name="entryIds"/>, from whichever playlists hold them.</summary>
    public static Scene Remove(Scene scene, IReadOnlyCollection<Guid> entryIds)
    {
        foreach (var id in entryIds)
            Holding(scene, id);
        if (entryIds.Count == 0)
            return scene;
        return scene with
        {
            Playlists = scene
                .Playlists.Select(p =>
                    p.Entries.Any(e => entryIds.Contains(e.Id))
                        ? p with
                        {
                            Entries = p.Entries.Where(e => !entryIds.Contains(e.Id)).ToArray(),
                        }
                        : p
                )
                .ToArray(),
        };
    }

    /// <summary>Puts the selected playlist's entries in <paramref name="order"/> (old indices).</summary>
    public static Scene Reorder(Scene scene, IReadOnlyList<int> order)
    {
        var playlist = Selected(scene);
        var entries = BlockMove.Apply(playlist.Entries, order);
        return entries.SequenceEqual(playlist.Entries)
            ? scene
            : WithPlaylist(scene, playlist with { Entries = entries });
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

    /// <summary>Sets how many times an entry in any playlist plays, 1 to <see cref="MaxLoops"/>, or null to follow its track.</summary>
    public static Scene SetLoops(Scene scene, Guid entryId, int? loops)
    {
        var playlist = Holding(scene, entryId);
        var index = ListEdit.IndexOf(playlist.Entries, e => e.Id == entryId);
        var clamped = loops is { } n ? Math.Clamp(n, 1, MaxLoops) : (int?)null;
        if (playlist.Entries[index].Loops == clamped)
            return scene;
        return WithPlaylist(
            scene,
            playlist with
            {
                Entries = ListEdit.Replace(playlist.Entries, index, playlist.Entries[index] with { Loops = clamped }),
            }
        );
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

    /// <summary>Sets whether Live wraps from the selected playlist's last entry to its first.</summary>
    public static Scene SetPlaylistLoops(Scene scene, bool loops) =>
        Selected(scene) is var playlist && playlist.Loops == loops
            ? scene
            : WithPlaylist(scene, playlist with { Loops = loops });

    /// <summary>The index of entry <paramref name="entryId"/> in the selected playlist, or −1.</summary>
    public static int IndexOf(Scene scene, Guid entryId) =>
        ListEdit.IndexOf(Selected(scene).Entries, e => e.Id == entryId);

    /// <summary>True when an entry holds the playlist for good: no loop count and a looping track with points.</summary>
    public static bool HoldsPlaylist(Scene scene, PlaylistEntry entry) =>
        entry.Loops is null && SceneEditing.Get(scene, entry.TrackId) is { Loop: true, Points.Count: > 0 };

    /// <summary>True when an entry in the selected playlist has a track with points, so Live has something to play.</summary>
    public static bool CanPlay(Scene scene) =>
        Selected(scene).Entries.Any(e => SceneEditing.Get(scene, e.TrackId).Points.Count > 0);

    /// <summary>The index of entry <paramref name="entryId"/> in the selected playlist, refusing an unknown one.</summary>
    public static int Require(Scene scene, Guid entryId) =>
        IndexOf(scene, entryId) is var index and >= 0 ? index : throw new ArgumentException(NoSuchEntry);

    /// <summary>The playlist holding entry <paramref name="entryId"/>, refusing an unknown one.</summary>
    private static Playlist Holding(Scene scene, Guid entryId) =>
        scene.Playlists.FirstOrDefault(p => p.Entries.Any(e => e.Id == entryId))
        ?? throw new ArgumentException(NoSuchEntry);

    /// <summary>The scene with <paramref name="playlist"/> in place of the playlist with its id.</summary>
    private static Scene WithPlaylist(Scene scene, Playlist playlist) =>
        scene with
        {
            Playlists = ListEdit.Replace(
                scene.Playlists,
                ListEdit.IndexOf(scene.Playlists, p => p.Id == playlist.Id),
                playlist
            ),
        };
}
