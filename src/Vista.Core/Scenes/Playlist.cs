namespace Vista.Core.Scenes;

/// <summary>A named sequence of entries a switchboard slot can play, and whether it starts again after the last.</summary>
public sealed record Playlist(Guid Id, string Name, IReadOnlyList<PlaylistEntry> Entries, bool Loops = false);
