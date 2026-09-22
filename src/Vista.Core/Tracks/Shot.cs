namespace Vista.Core.Tracks;

/// <summary>Something the Director can put on program: a track, a playlist, or the game's own camera.</summary>
public abstract record Shot;

/// <summary>A track shot: play <see cref="Track"/> through its timing curve.</summary>
public sealed record TrackShot(Track Track) : Shot;

/// <summary>A playlist shot: play its entries in turn, wrapping to the first at the end when <paramref name="Loops"/>.</summary>
public sealed record PlaylistShot(IReadOnlyList<PlaylistItem> Items, bool Loops = false) : Shot;

/// <summary>Hands the camera back to the game.</summary>
public sealed record GameCameraShot : Shot;
