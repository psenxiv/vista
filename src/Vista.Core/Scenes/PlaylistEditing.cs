using System.Globalization;
using Vista.Core.Editing;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's playlists and their entries: create, rename, duplicate, delete and select a playlist; add, remove, reorder and loop counts; and what Live can play.</summary>
public static class PlaylistEditing
{
    private const string Stem = "Playlist";

    /// <summary>The name of a new scene's playlist.</summary>
    public const string FirstName = $"{Stem} 1";

    /// <summary>The most times an entry can play its track.</summary>
    public const int MaxLoops = 99;

    /// <summary>Why a playlist entry Id can't be used: no entry has it.</summary>
    public const string NoSuchEntry = "There is no such playlist entry.";

    /// <summary>Why a playlist name can't be used: another playlist has it.</summary>
    public const string NameTaken = "A playlist with that name exists.";

    /// <summary>Why a playlist Id can't be used: no playlist has it.</summary>
    public const string NoSuchPlaylist = "There is no such playlist.";

    /// <summary>Why a playlist can't be deleted: it is the only one.</summary>
    public const string LastPlaylist = "The last playlist can't be deleted.";

    /// <summary>An empty playlist named <see cref="FirstName"/>, with a new id.</summary>
    public static Playlist Empty() => new(Guid.NewGuid(), FirstName, []);

    /// <summary>The selected playlist, which Live plays and the Playlist panel shows.</summary>
    public static Playlist Selected(Scene scene) => scene.Playlists.First(p => p.Id == scene.SelectedPlaylistId);

    /// <summary>The playlist <paramref name="id"/>, refusing an unknown one.</summary>
    public static Playlist Get(Scene scene, Guid id) =>
        scene.Playlists.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException(NoSuchPlaylist);

    /// <summary>Why <paramref name="name"/> can't name a playlist, or null; playlist <paramref name="renaming"/> may take any case of its own name, as a scene may.</summary>
    public static string? NameRefusal(Scene scene, string name, Guid? renaming = null)
    {
        var own = scene.Playlists.FirstOrDefault(p => p.Id == renaming)?.Name;
        var same = own is not null && string.Equals(name.Trim(), own, StringComparison.OrdinalIgnoreCase);
        return SceneNames.LengthRefusal(name) ?? (!same && SceneNames.Taken(name, Names(scene)) ? NameTaken : null);
    }

    /// <summary>The first "Playlist n" no playlist has.</summary>
    public static string NewSuggestion(Scene scene) => SceneNames.NextFree(Stem, Names(scene));

    /// <summary>A name for a copy of playlist <paramref name="id"/> that no playlist has.</summary>
    public static string CopySuggestion(Scene scene, Guid id) => SceneNames.CopyOf(Get(scene, id).Name, Names(scene));

    /// <summary>True when the scene has more than one playlist.</summary>
    public static bool CanDelete(Scene scene) => scene.Playlists.Count > 1;

    /// <summary>Selects playlist <paramref name="id"/>, refusing an unknown one.</summary>
    public static Scene Select(Scene scene, Guid id)
    {
        Get(scene, id);
        return scene.SelectedPlaylistId == id ? scene : scene with { SelectedPlaylistId = id };
    }

    /// <summary>Appends an empty playlist named <paramref name="name"/>, trimmed, and selects it.</summary>
    public static Scene New(Scene scene, string name)
    {
        RequireName(scene, name);
        var added = new Playlist(Guid.NewGuid(), name.Trim(), []);
        return scene with
        {
            Playlists = ListEdit.Insert(scene.Playlists, scene.Playlists.Count, added),
            SelectedPlaylistId = added.Id,
        };
    }

    /// <summary>Renames playlist <paramref name="id"/> to <paramref name="name"/>, trimmed.</summary>
    public static Scene Rename(Scene scene, Guid id, string name)
    {
        var playlist = Get(scene, id);
        RequireName(scene, name, id);
        return playlist.Name == name.Trim() ? scene : WithPlaylist(scene, playlist with { Name = name.Trim() });
    }

    /// <summary>Appends a copy of playlist <paramref name="id"/> named <paramref name="name"/>, trimmed, with new ids; selects the copy when the source was selected.</summary>
    public static Scene Duplicate(Scene scene, Guid id, string name)
    {
        var source = Get(scene, id);
        RequireName(scene, name);
        var copy = source with
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Entries = source.Entries.Select(e => e with { Id = Guid.NewGuid() }).ToArray(),
        };
        return scene with
        {
            Playlists = ListEdit.Insert(scene.Playlists, scene.Playlists.Count, copy),
            SelectedPlaylistId = scene.SelectedPlaylistId == id ? copy.Id : scene.SelectedPlaylistId,
        };
    }

    /// <summary>Deletes playlist <paramref name="id"/>, refusing the last; deleting the selected one selects the first remaining by name.</summary>
    public static Scene Delete(Scene scene, Guid id)
    {
        Get(scene, id);
        if (!CanDelete(scene))
            throw new ArgumentException(LastPlaylist);
        var remaining = scene.Playlists.Where(p => p.Id != id).ToArray();
        return scene with
        {
            Playlists = remaining,
            SelectedPlaylistId =
                scene.SelectedPlaylistId == id
                    ? remaining.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).First().Id
                    : scene.SelectedPlaylistId,
        };
    }

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
                .Playlists.Select(p => p with { Entries = p.Entries.Where(e => !entryIds.Contains(e.Id)).ToArray() })
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

    /// <summary>Throws the <see cref="NameRefusal"/> for <paramref name="name"/>, if any.</summary>
    private static void RequireName(Scene scene, string name, Guid? renaming = null)
    {
        if (NameRefusal(scene, name, renaming) is { } refusal)
            throw new ArgumentException(refusal);
    }

    /// <summary>The names of the scene's playlists.</summary>
    private static IEnumerable<string> Names(Scene scene) => scene.Playlists.Select(p => p.Name);

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
