namespace CinematicCam.Core;

/// <summary>Something the Director can put on program: a track, a snap point, or the game's own camera.</summary>
public abstract record Shot;

/// <summary>A track shot: play <see cref="Track"/> through its timing curve.</summary>
public sealed record TrackShot(Track Track) : Shot;

/// <summary>A snap shot: hold a single captured pose.</summary>
public sealed record SnapShot(SnapPoint Point) : Shot;

/// <summary>Hands the camera back to the game.</summary>
public sealed record GameCameraShot : Shot;
