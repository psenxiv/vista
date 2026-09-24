using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Editing;

/// <summary>Converts a camera pose to and from the matrix a gizmo edits: rows right, up, backward, then translation.</summary>
public static class PoseMatrix
{
    /// <summary>The gizmo matrix for a camera pose.</summary>
    public static Matrix4x4 From(Vector3 position, float yaw, float pitch, float roll)
    {
        var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));
        var up = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward, roll));
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        var back = -forward;

        return new Matrix4x4(
            right.X,
            right.Y,
            right.Z,
            0f,
            up.X,
            up.Y,
            up.Z,
            0f,
            back.X,
            back.Y,
            back.Z,
            0f,
            position.X,
            position.Y,
            position.Z,
            1f
        );
    }

    /// <summary>The camera pose a gizmo matrix describes, with pitch clamped to <see cref="TrackAim.PitchLimit"/>.</summary>
    public static (Vector3 Position, float Yaw, float Pitch, float Roll) ToPose(Matrix4x4 matrix)
    {
        var position = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        var forward = -Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33));
        var up = Vector3.Normalize(new Vector3(matrix.M21, matrix.M22, matrix.M23));

        var (yaw, pitch) = TrackAim.FromDirection(forward);
        var level = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward));
        var roll = MathF.Atan2(Vector3.Dot(Vector3.Cross(level, up), forward), Vector3.Dot(level, up));

        return (position, yaw, Math.Clamp(pitch, -TrackAim.PitchLimit, TrackAim.PitchLimit), roll);
    }
}
