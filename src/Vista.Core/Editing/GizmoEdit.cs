using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;

namespace Vista.Core.Editing;

/// <summary>One gimbal rotate ring; each changes one angle of a point.</summary>
public enum GimbalRing { Yaw, Pitch, Roll }

/// <summary>Turns a dragged gizmo matrix into the control point it describes.</summary>
public static class GizmoEdit
{
    /// <summary>Changes below this, in metres or radians, are matrix round-off, not a drag.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>The frame a ring turns in: level for yaw, yaw and pitch for pitch, the full aim for roll.</summary>
    public static Matrix4x4 RingFrame(ControlPoint point, GimbalRing ring) => ring switch
    {
        GimbalRing.Yaw => PoseMatrix.From(point.Position, point.Yaw, 0f, 0f),
        GimbalRing.Pitch => PoseMatrix.From(point.Position, point.Yaw, point.Pitch, 0f),
        _ => PoseMatrix.From(point.Position, point.Yaw, point.Pitch, point.Roll),
    };

    /// <summary>The point moved to the dragged matrix's position, or <paramref name="original"/> itself when it did not move.</summary>
    public static ControlPoint Move(ControlPoint original, Matrix4x4 dragged)
    {
        var position = dragged.Translation;
        return Vector3.Distance(position, original.Position) <= Tolerance ? original : original with { Position = position };
    }

    /// <summary>The point with one angle taken from its dragged ring frame, or <paramref name="original"/> itself when it did not turn.</summary>
    public static ControlPoint Rotate(ControlPoint original, GimbalRing ring, Matrix4x4 dragged)
    {
        var forward = -new Vector3(dragged.M31, dragged.M32, dragged.M33);
        switch (ring)
        {
            case GimbalRing.Yaw:
                var yaw = TrackAim.FromDirection(forward).Yaw;
                return Same(yaw, original.Yaw) ? original : original with { Yaw = yaw };

            case GimbalRing.Pitch:
                var level = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, original.Yaw, 0f));
                var pitch = Math.Clamp(MathF.Atan2(forward.Y, Vector3.Dot(forward, level)), -TrackAim.PitchLimit, TrackAim.PitchLimit);
                return Same(pitch, original.Pitch) ? original : original with { Pitch = pitch };

            default:
                var roll = PoseMatrix.ToPose(dragged).Roll;
                return Same(roll, original.Roll) ? original : original with { Roll = roll };
        }
    }

    private static bool Same(float a, float b) => MathF.Abs(Angles.Wrap(a - b)) <= Tolerance;
}
