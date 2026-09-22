namespace Vista.Core.Tracks;

/// <summary>A camera move: a path through control points, a timing curve, and how it aims.</summary>
public sealed record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<TimingKey> Timing, AimMode Aim, PlaybackMode Playback);
