using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Editing;

/// <summary>Converts a camera pose to and from the matrix a gizmo edits: rows right, up, backward, then translation.</summary>
public static class PoseMatrix
{
    /// <summary>The gizmo matrix for a camera pose.</summary>
    public static Matrix4x4 From(Vector3 position, float yaw, float pitch, float roll)
    {
        var rotation = CameraRotation.FromAngles(yaw, pitch, roll);
        var forward = CameraRotation.Forward(rotation);
        var up = CameraRotation.Up(rotation);
        var right = Vector3.Cross(forward, up);
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

    /// <summary>The camera pose a gizmo matrix describes.</summary>
    public static (Vector3 Position, float Yaw, float Pitch, float Roll) ToPose(Matrix4x4 matrix)
    {
        var position = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        var forward = -Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33));
        var up = Vector3.Normalize(new Vector3(matrix.M21, matrix.M22, matrix.M23));

        var (yaw, pitch, roll) = CameraRotation.ToAngles(CameraRotation.FromBasis(forward, up));
        return (position, yaw, pitch, roll);
    }
}
