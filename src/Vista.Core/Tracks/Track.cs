using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points local to its anchor, their timing, speed, aim and playback, its Look At point, the character it watches or follows and how it follows.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false, Vector3 LookAt = default, bool LookAtPlaced = false, string? TargetName = null, string? TargetWorld = null, float AimHeight = TrackEditing.DefaultAimHeight, float Smoothing = TrackEditing.DefaultSmoothing, bool FollowTurns = true, bool FollowLooks = true);
