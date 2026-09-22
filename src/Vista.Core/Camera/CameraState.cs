using System.Numerics;

namespace CinematicCam.Core.Camera;

/// <summary>Where the camera is, what it looks at, its field of view, and its roll in radians (positive rolls right).</summary>
public readonly record struct CameraState(Vector3 Position, Vector3 LookAt, float Fov, float Roll = 0f);
