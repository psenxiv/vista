using System.Numerics;

namespace CinematicCam.Core.Tracks;

/// <summary>A point a track's path passes through, with the look, field of view and roll captured there.</summary>
public sealed record ControlPoint(Vector3 Position, float Yaw, float Pitch, float Fov, float Roll = 0f);
