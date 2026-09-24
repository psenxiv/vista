using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Where the camera is, what it looks at, which way is up in the picture (a unit vector square to the view), and its field of view.</summary>
public readonly record struct CameraState(Vector3 Position, Vector3 LookAt, Vector3 Up, float Fov)
{
    /// <summary>The picture's roll about the view in radians, positive rolling right; 0 when facing straight up or down.</summary>
    public float Roll => CameraRotation.ToAngles(CameraRotation.FromBasis(LookAt - Position, Up)).Roll;

    /// <summary>A camera at <paramref name="position"/> turned by <paramref name="rotation"/>.</summary>
    public static CameraState FromRotation(Vector3 position, Quaternion rotation, float fov) =>
        new(
            position,
            position + (CameraRotation.Forward(rotation) * FreeCamMotion.LookAtDistance),
            CameraRotation.Up(rotation),
            fov
        );

    /// <summary>A camera at <paramref name="position"/> facing <paramref name="yaw"/> and <paramref name="pitch"/>, rolled by <paramref name="roll"/>.</summary>
    public static CameraState FromAngles(Vector3 position, float yaw, float pitch, float roll, float fov) =>
        new(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraRotation.Up(CameraRotation.FromAngles(yaw, pitch, roll)),
            fov
        );
}
