namespace Vista.Core.Tracks;

/// <summary>A camera move: a path through control points, their timing, the track's speed, and how it aims.</summary>
public sealed record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackMode Playback);
