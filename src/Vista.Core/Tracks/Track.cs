namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points, their timing, the track's speed, how it aims and how it plays.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop);
