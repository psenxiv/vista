using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using static System.FormattableString;

namespace Vista.Core.Display;

/// <summary>The scrub bar over the whole of slot <paramref name="program"/>'s shot, or the stretch of it <paramref name="view"/> zooms to: where times fall along it, its pass ticks, and its entries' labels.</summary>
public sealed class PlaylistScrub(PlaylistTimeline timeline, Scene scene, Slot program, TimingView? view = null)
{
    private readonly TimingView shown = view ?? TimingView.Whole((float)timeline.Total);

    /// <summary>How far along the bar <paramref name="time"/> falls, from 0 to 1, held at the ends outside the view; 0 when the view has no length.</summary>
    public float FractionOf(double time) =>
        shown.Span > 0f ? (float)Math.Clamp((time - shown.From) / shown.Span, 0.0, 1.0) : 0f;

    /// <summary>Whether any of <paramref name="segment"/> is within the view.</summary>
    public bool Visible(PlaylistSegment segment) => segment.End > shown.From && segment.Start < shown.To;

    /// <summary>Whether <paramref name="time"/> is within the view, so the head shows.</summary>
    public bool Shows(double time) => time >= shown.From && time <= shown.To;

    /// <summary>The playlist time <paramref name="fraction"/> of the way along the bar.</summary>
    public double TimeAt(float fraction) => shown.From + (Fraction.Clamp(fraction) * (double)shown.Span);

    /// <summary>The segment <paramref name="fraction"/> of the way along the bar.</summary>
    public PlaylistSegment SegmentAt(float fraction) => timeline.Segments[timeline.At(TimeAt(fraction)).Index];

    /// <summary>Where each of <paramref name="segment"/>'s passes after the first starts, as fractions of the bar, for those within the view.</summary>
    public IEnumerable<float> PassTicks(PlaylistSegment segment) =>
        Enumerable
            .Range(1, segment.Passes - 1)
            .Select(k => segment.Start + (k * segment.PassLength))
            .Where(Shows)
            .Select(FractionOf);

    /// <summary>A playlist's segment as its entry's number in the playlist and its track's name, as "3 · Hairpin"; a track slot's as the name the slot shows; null when the scene no longer has them.</summary>
    public string? Label(PlaylistSegment segment)
    {
        if (program.TrackId is { } trackId)
            return SceneEditing.TryGet(scene, trackId, out _) ? SwitchboardEditing.NameOf(scene, program) : null;
        var entries =
            program.PlaylistId is { } playlistId && PlaylistEditing.TryGet(scene, playlistId, out var playlist)
                ? playlist.Entries
                : [];
        var index = ListEdit.IndexOf(entries, e => e.Id == segment.EntryId);
        return index >= 0 && SceneEditing.TryGet(scene, entries[index].TrackId, out var track)
            ? Invariant($"{index + 1} · {track.Name}")
            : null;
    }
}
