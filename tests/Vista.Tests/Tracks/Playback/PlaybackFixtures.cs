using CsCheck;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Playback;

/// <summary>Tracks the playback tests play.</summary>
internal static class PlaybackFixtures
{
    // Points at x = 0, 5, 10 in two 5 s legs: a 10 s track.
    internal static Track StraightTrack(bool loop = false, PlaybackDirection direction = PlaybackDirection.Forward)
    {
        var track = TrackEditing.SetDirection(
            TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop),
            direction
        );
        foreach (var x in new[] { 0f, 5f, 10f })
            track = TrackEditing.Append(track, Point(x));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    // Points at x = 0 and 10 in one leg of the given seconds.
    internal static Track OneLeg(float seconds, bool loop = false) =>
        TrackEditing.SetLegDuration(
            WithTwoPoints(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop)),
            1,
            seconds
        );

    /// <summary>A playlist item playing <paramref name="track"/> under a new entry id, <paramref name="loops"/> times or following the track.</summary>
    internal static PlaylistItem Item(Track track, int? loops = null) => new(Guid.NewGuid(), track, loops);

    /// <summary>A generated path track, looping or not, played any way round.</summary>
    internal static readonly Gen<Track> AnyPlayedTrack =
        from track in AnyPathTrack
        from loop in Gen.Bool
        from direction in Gen.Enum<PlaybackDirection>()
        select TrackEditing.SetDirection(TrackEditing.SetLoop(track, loop), direction);

    /// <summary>One to three generated tracks and a playlist of one to five of them, each playing once, repeated or following its track, looping or not, built as the Playlist panel builds it.</summary>
    internal static readonly Gen<Scene> AnyPlaylistScene =
        from tracks in AnyPlayedTrack.Array[1, 3]
        // Per entry: which track, and 0 to follow the track or a repeat count.
        from entries in Gen.Select(Gen.Int[0, tracks.Length - 1], Gen.Int[0, 3]).Array[1, 5]
        from loops in Gen.Bool
        select PlaylistScene(tracks, entries, loops);

    /// <summary>A scene of <paramref name="tracks"/> whose playlist plays each entry's track its count of times (0 follows the track).</summary>
    private static Scene PlaylistScene(Track[] tracks, (int Track, int Loops)[] entries, bool loops)
    {
        var scene = OnePlaylist(tracks);
        foreach (var (track, count) in entries)
        {
            scene = PlaylistEditing.Add(scene, [tracks[track].Id]);
            scene = PlaylistEditing.SetLoops(scene, Entries(scene)[^1].Id, count == 0 ? null : count);
        }

        return PlaylistEditing.SetPlaylistLoops(scene, loops);
    }
}
