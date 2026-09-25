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
        var matrix = CameraRotation.Basis(CameraRotation.Forward(rotation), CameraRotation.Up(rotation));
        matrix.Translation = position;
        return matrix;
    }

    /// <summary>The camera pose a gizmo matrix describes.</summary>
    public static (Vector3 Position, float Yaw, float Pitch, float Roll) ToPose(Matrix4x4 matrix)
    {
        var (yaw, pitch, roll) = CameraRotation.ToAngles(CameraRotation.FromBasis(Forward(matrix), Up(matrix)));
        return (matrix.Translation, yaw, pitch, roll);
    }

    /// <summary>The unit forward a gizmo matrix faces: its backward row, negated.</summary>
    public static Vector3 Forward(Matrix4x4 matrix) =>
        -Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33));

    /// <summary>A gizmo matrix's unit up row.</summary>
    public static Vector3 Up(Matrix4x4 matrix) => Vector3.Normalize(new Vector3(matrix.M21, matrix.M22, matrix.M23));
}
