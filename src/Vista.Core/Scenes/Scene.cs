using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, which are hidden, the playlists and which one is selected, the switchboard, and the anchor they hang off.</summary>
public sealed record Scene(
    IReadOnlyList<Track> Tracks,
    IReadOnlySet<Guid> Hidden,
    IReadOnlyList<Playlist> Playlists,
    Guid SelectedPlaylistId,
    Switchboard Switchboard,
    Anchor Anchor = default,
    bool AnchorPlaced = false
);
