namespace Vista.Core.Tracks.Playback;

/// <summary>The playlist laid end to end as Live plays it, and playlist time to and from an entry, pass and time.</summary>
public sealed class PlaylistTimeline
{
    /// <summary>Lays out <paramref name="items"/> whose tracks run for <paramref name="durations"/> seconds, stopping after an entry that loops forever.</summary>
    internal PlaylistTimeline(IReadOnlyList<PlaylistItem> items, IReadOnlyList<double> durations, bool loops)
    {
        var segments = new List<PlaylistSegment>();
        var start = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var forever = item.Loops is null && item.Track.Loop;
            var passes = item.Loops is { } n ? Math.Max(n, 1) : 1;
            var segment = new PlaylistSegment(
                i,
                item.EntryId,
                start,
                PlaybackClock.CycleLength(item.Track.Direction, durations[i]),
                passes,
                forever
            );
            segments.Add(segment);
            start = segment.End;
            if (forever)
                break;
        }

        Segments = segments;
        Total = start;
        Wraps = loops && !segments[^1].LoopsForever;
    }

    /// <summary>One segment per entry in playing order, up to and including an entry that loops forever.</summary>
    public IReadOnlyList<PlaylistSegment> Segments { get; }

    /// <summary>The whole timeline's length in seconds.</summary>
    public double Total { get; }

    /// <summary>True when the playhead wraps from the end back to the start: the playlist loops and doesn't end on an entry looping forever.</summary>
    public bool Wraps { get; }

    /// <summary>The entry, pass and time at <paramref name="time"/>, clamped to the timeline; a segment's end is the next one's start, except at the very end.</summary>
    public PlaylistPosition At(double time)
    {
        var t = double.IsNaN(time) ? 0.0 : Math.Max(time, 0.0);
        if (t >= Total)
        {
            var last = Segments[^1];
            return new PlaylistPosition(last.Index, last.Passes - 1, last.PassLength);
        }

        var index = Segments.Count - 1;
        while (index > 0 && Segments[index].Start > t)
            index--;
        var segment = Segments[index];

        var into = t - segment.Start;
        var pass =
            segment.PassLength > 0.0
                ? (int)Math.Clamp(Math.Floor(into / segment.PassLength), 0, segment.Passes - 1)
                : 0;
        return new PlaylistPosition(
            index,
            pass,
            Math.Clamp(into - (pass * segment.PassLength), 0.0, segment.PassLength)
        );
    }

    /// <summary>The playlist time at <paramref name="position"/>.</summary>
    public double TimeOf(PlaylistPosition position)
    {
        var segment = Segments[position.Index];
        return segment.Start + (position.Pass * segment.PassLength) + position.Time;
    }
}
