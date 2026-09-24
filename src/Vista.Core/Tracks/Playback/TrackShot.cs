namespace Vista.Core.Tracks.Playback;

/// <summary>A track shot: play <see cref="Track"/> through its timing curve.</summary>
public sealed record TrackShot(Track Track) : Shot;
