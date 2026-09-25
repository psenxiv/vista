using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;

namespace Vista.Core.Tracks;

/// <summary>A point a track's path passes through, with the look, field of view and roll captured there.</summary>
public sealed record ControlPoint(Vector3 Position, float Yaw, float Pitch, float Fov, float Roll = 0f)
{
    /// <summary>The field of view a shot plays this point at: its own, kept within the editor's range.</summary>
    public float PlayedFov => Math.Clamp(Fov, EditLimits.MinFov, EditLimits.MaxFov);

    /// <summary>A camera at <paramref name="position"/> turned by <paramref name="rotation"/> as a point.</summary>
    public static ControlPoint FromRotation(Vector3 position, Quaternion rotation, float fov)
    {
        var (yaw, pitch, roll) = CameraRotation.ToAngles(rotation);
        return new ControlPoint(position, yaw, pitch, fov, roll);
    }

    /// <summary>A camera frame as a point: where it is, where it faces, its roll and field of view.</summary>
    public static ControlPoint FromFrame(CameraState frame) =>
        FromRotation(frame.Position, CameraRotation.FromBasis(frame.LookAt - frame.Position, frame.Up), frame.Fov);
}
