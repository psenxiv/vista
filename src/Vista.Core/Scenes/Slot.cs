namespace Vista.Core.Scenes;

/// <summary>A switchboard slot: its own name, or null while it follows its track or playlist's, and the one track or playlist it cuts to; exactly one id is set.</summary>
public sealed record Slot(string? Name, Guid? TrackId, Guid? PlaylistId);
