namespace Vista.Core.Tracks.Playback;

/// <summary>One playlist entry ready to play: its entry, its track in the world, and how many times (null follows the track).</summary>
public sealed record PlaylistItem(Guid EntryId, Track Track, int? Loops);
