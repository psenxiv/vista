using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, which of them are hidden, and the anchor they hang off.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, Anchor Anchor = default, bool AnchorPlaced = false);
