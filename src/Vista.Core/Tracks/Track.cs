namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points local to its anchor, their timing, speed, aim and playback.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false);
