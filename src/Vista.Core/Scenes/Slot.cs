namespace Vista.Core.Scenes;

/// <summary>A switchboard slot: its name and the one track or playlist it cuts to; exactly one id is set.</summary>
public sealed record Slot(string Name, Guid? TrackId, Guid? PlaylistId);
