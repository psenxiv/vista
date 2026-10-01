using Vista.Core.Scenes;
using static System.FormattableString;

namespace Vista.Core.Display;

/// <summary>The Playlist panel's heading while Live plays the selected playlist.</summary>
public static class PlaylistHeading
{
    /// <summary>The entry Live is playing as its number in the selected playlist, the playlist's count and its track's name, as "2 / 5 — Hairpin"; null when the playlist doesn't hold it.</summary>
    public static string? NowPlaying(Scene scene, Guid entryId)
    {
        var entries = PlaylistEditing.Selected(scene).Entries;
        var index = PlaylistEditing.IndexOf(scene, entryId);
        return index >= 0 && SceneEditing.TryGet(scene, entries[index].TrackId, out var track)
            ? Invariant($"{index + 1} / {entries.Count} — {track.Name}")
            : null;
    }
}
