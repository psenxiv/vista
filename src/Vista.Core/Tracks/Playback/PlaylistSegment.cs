namespace Vista.Core.Tracks.Playback;

/// <summary>One entry's stretch of the playlist timeline: where it starts, how long one pass is, and how many passes it plays.</summary>
public sealed record PlaylistSegment(
    int Index,
    Guid EntryId,
    double Start,
    double PassLength,
    int Passes,
    bool LoopsForever
)
{
    /// <summary>The segment's length: its passes laid end to end, or one pass for an entry looping forever.</summary>
    public double Length => PassLength * Passes;

    /// <summary>Where the segment ends in playlist time.</summary>
    public double End => Start + Length;
}
