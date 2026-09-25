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
}
