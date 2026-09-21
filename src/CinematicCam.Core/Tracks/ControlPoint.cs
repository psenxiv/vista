using System.Numerics;

namespace CinematicCam.Core.Tracks;

/// <summary>A point a track's path passes through, with the look and field of view captured there.</summary>
public sealed record ControlPoint(Vector3 Position, float Yaw, float Pitch, float Fov);
