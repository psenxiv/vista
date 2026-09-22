using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, and which of them are hidden.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden);
