using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using static System.FormattableString;

namespace Vista.Core.Display;

/// <summary>Live's scrub bar over the whole playlist: where times fall along it, its pass ticks, and the text it shows.</summary>
public sealed class PlaylistScrub(PlaylistTimeline timeline, Scene scene)
{
    /// <summary>The playlist laid end to end, one segment per entry.</summary>
    public PlaylistTimeline Timeline => timeline;

    /// <summary>How far along the bar <paramref name="time"/> falls, from 0 to 1; 0 when the playlist has no length.</summary>
    public float FractionOf(double time) =>
        timeline.Total > 0.0 ? (float)Math.Clamp(time / timeline.Total, 0.0, 1.0) : 0f;

    /// <summary>The playlist time <paramref name="fraction"/> of the way along the bar.</summary>
    public double TimeAt(float fraction) => Fraction.Clamp(fraction) * timeline.Total;

    /// <summary>The segment <paramref name="fraction"/> of the way along the bar.</summary>
    public PlaylistSegment SegmentAt(float fraction) => timeline.Segments[timeline.At(TimeAt(fraction)).Index];

    /// <summary>Where each of <paramref name="segment"/>'s passes after the first starts, as fractions of the bar.</summary>
    public IEnumerable<float> PassTicks(PlaylistSegment segment) =>
        Enumerable.Range(1, segment.Passes - 1).Select(k => FractionOf(segment.Start + (k * segment.PassLength)));

    /// <summary>The segment's entry number in the playlist and its track's name, as "3 · Hairpin"; null when the scene no longer has them.</summary>
    public string? Label(PlaylistSegment segment) =>
        Entry(segment) is (var number, var name) ? Invariant($"{number} · {name}") : null;

    /// <summary>Segment <paramref name="playing"/>'s label, the time into its entry over the entry's length, then <paramref name="head"/> over the playlist's length; the track name is cut to fit <paramref name="width"/> as <paramref name="measure"/> measures text.</summary>
    public string Readout(int playing, double head, float width, Func<string, float> measure)
    {
        var segment = timeline.Segments[playing];
        var times = Invariant(
            $"{Units.SecondsValue(head - segment.Start)} / {Units.Seconds(segment.Length)}   |   {Units.SecondsValue(head)} / {Units.Seconds(timeline.Total)}"
        );
        if (Entry(segment) is not (var number, var name))
            return times;

        var prefix = Invariant($"{number} · ");
        var suffix = "  " + times;
        return prefix + RowFit.Ellipsis(name, width - measure(prefix + suffix), measure) + suffix;
    }

    /// <summary>The segment's entry as its 1-based place in the scene's playlist and its track's name, or null when either is gone.</summary>
    private (int Number, string Name)? Entry(PlaylistSegment segment)
    {
        var index = PlaylistEditing.IndexOf(scene, segment.EntryId);
        return index >= 0 && SceneEditing.TryGet(scene, scene.Playlist[index].TrackId, out var track)
            ? (index + 1, track.Name)
            : null;
    }
}
