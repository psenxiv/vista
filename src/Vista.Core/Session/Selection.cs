namespace Vista.Core.Session;

/// <summary>What's selected besides the edited track: its points, other tracks, or playlist entries; at most one kind at a time.</summary>
public sealed record Selection(IReadOnlyList<int> Points, IReadOnlyList<Guid> Tracks, IReadOnlyList<Guid> Entries)
{
    /// <summary>Nothing selected but the edited track.</summary>
    public static readonly Selection None = new([], [], []);
}
