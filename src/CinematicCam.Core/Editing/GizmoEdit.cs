using System.Numerics;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>Turns a dragged gizmo matrix into the control point it describes.</summary>
public static class GizmoEdit
{
    /// <summary>Changes below this, in metres or radians, are matrix round-off, not a drag.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>The edited point, or <paramref name="original"/> itself when the drag changed nothing.</summary>
    public static ControlPoint Apply(ControlPoint original, Matrix4x4 dragged, GizmoMode mode, AimMode aim)
    {
        if (mode == GizmoMode.Move)
        {
            var position = dragged.Translation;
            return Vector3.Distance(position, original.Position) <= Tolerance ? original : original with { Position = position };
        }

        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(dragged);
        var edited = aim == AimMode.AimKeys
            ? original with { Yaw = yaw, Pitch = pitch, Roll = roll }
            : original with { Roll = roll };

        return Same(edited.Yaw, original.Yaw) && Same(edited.Pitch, original.Pitch) && Same(edited.Roll, original.Roll)
            ? original
            : edited;
    }

    private static bool Same(float a, float b) => MathF.Abs(MathF.IEEERemainder(a - b, MathF.Tau)) <= Tolerance;
}
