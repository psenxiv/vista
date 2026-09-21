using System.Numerics;

namespace CinematicCam.Core.Camera;

/// <summary>Where the camera is, what it looks at, and its field of view.</summary>
public readonly record struct CameraState(Vector3 Position, Vector3 LookAt, float Fov);
