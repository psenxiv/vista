using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using static System.FormattableString;

namespace Vista.Core.Display;

/// <summary>Live's scrub bar over the whole playlist, or the stretch of it <paramref name="view"/> zooms to: where times fall along it, its pass ticks, and its entries' labels.</summary>
public sealed class PlaylistScrub(PlaylistTimeline timeline, Scene scene, TimingView? view = null)
{
    private readonly TimingView shown = view ?? TimingView.Whole((float)timeline.Total);

    /// <summary>The playlist laid end to end, one segment per entry.</summary>
    public PlaylistTimeline Timeline => timeline;

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

    /// <summary>The segment's entry number in the playlist and its track's name, as "3 · Hairpin"; null when the scene no longer has them.</summary>
    public string? Label(PlaylistSegment segment) =>
        Entry(segment) is (var number, var name) ? Invariant($"{number} · {name}") : null;

    /// <summary>The segment's entry as its 1-based place in the scene's playlist and its track's name, or null when either is gone.</summary>
    private (int Number, string Name)? Entry(PlaylistSegment segment)
    {
        var index = PlaylistEditing.IndexOf(scene, segment.EntryId);
        return index >= 0 && SceneEditing.TryGet(scene, scene.Playlist[index].TrackId, out var track)
            ? (index + 1, track.Name)
            : null;
    }
}
