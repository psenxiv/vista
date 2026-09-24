using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, which are hidden, the playlist Live plays and whether it loops, and the anchor they hang off.</summary>
public sealed record Scene(
    IReadOnlyList<Track> Tracks,
    IReadOnlySet<Guid> Hidden,
    IReadOnlyList<PlaylistEntry> Playlist,
    Anchor Anchor = default,
    bool AnchorPlaced = false,
    bool PlaylistLoops = false
);
