using System.Numerics;

namespace CinematicCam.Core.Tracks;

/// <summary>A single captured camera pose, held distinct from a track so degenerate cases never reach the spline.</summary>
public sealed record SnapPoint(Vector3 Position, float Yaw, float Pitch, float Fov, float Roll = 0f);
