using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Editing;

/// <summary>Turns a dragged gizmo matrix into the control point it describes.</summary>
public static class GizmoEdit
{
    /// <summary>Changes below this, in metres or radians, are matrix round-off, not a drag.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>The frame a ring turns in: level for yaw, yaw and pitch for pitch, the full aim for roll.</summary>
    public static Matrix4x4 RingFrame(ControlPoint point, GimbalRing ring) =>
        ring switch
        {
            GimbalRing.Yaw => PoseMatrix.From(point.Position, point.Yaw, 0f, 0f),
            GimbalRing.Pitch => PoseMatrix.From(point.Position, point.Yaw, point.Pitch, 0f),
            _ => PoseMatrix.From(point.Position, point.Yaw, point.Pitch, point.Roll),
        };

    /// <summary>The point moved to the dragged matrix's position, or <paramref name="original"/> itself when it did not move.</summary>
    public static ControlPoint Move(ControlPoint original, Matrix4x4 dragged)
    {
        var position = dragged.Translation;
        return Vector3.Distance(position, original.Position) <= Tolerance
            ? original
            : original with
            {
                Position = position,
            };
    }

    /// <summary>The point turned by its dragged ring frame — yaw or roll alone, or all three for pitch, since going over the top turns them together — or <paramref name="original"/> itself when it did not turn.</summary>
    public static ControlPoint Rotate(ControlPoint original, GimbalRing ring, Matrix4x4 dragged)
    {
        var forward = -Vector3.Normalize(new Vector3(dragged.M31, dragged.M32, dragged.M33));
        switch (ring)
        {
            case GimbalRing.Yaw:
                var yaw = TrackAim.FromDirection(forward).Yaw;
                return Same(yaw, original.Yaw) ? original : original with { Yaw = yaw };

            case GimbalRing.Pitch:
                // The ring frame carries roll 0; re-roll its dragged up by the original roll before reading
                // the angles back, so a drag past vertical goes over the top instead of clamping.
                var ringUp = new Vector3(dragged.M21, dragged.M22, dragged.M23);
                var rolledUp = Vector3.Transform(ringUp, Quaternion.CreateFromAxisAngle(forward, original.Roll));
                var (pitchYaw, pitch, pitchRoll) = CameraRotation.ToAngles(CameraRotation.FromBasis(forward, rolledUp));
                var turned = original with
                {
                    Yaw = Same(pitchYaw, original.Yaw) ? original.Yaw : pitchYaw,
                    Pitch = Same(pitch, original.Pitch) ? original.Pitch : pitch,
                    Roll = Same(pitchRoll, original.Roll) ? original.Roll : pitchRoll,
                };
                return turned == original ? original : turned;

            default:
                var roll = PoseMatrix.ToPose(dragged).Roll;
                return Same(roll, original.Roll) ? original : original with { Roll = roll };
        }
    }

    /// <summary>The anchor turned to the dragged matrix's yaw when <paramref name="rotate"/>, else moved to its position.</summary>
    public static Anchor MoveAnchor(Anchor start, Matrix4x4 dragged, bool rotate) =>
        rotate
            ? start with
            {
                Yaw = TrackAim.FromDirection(-new Vector3(dragged.M31, dragged.M32, dragged.M33)).Yaw,
            }
            : start with
            {
                Position = dragged.Translation,
            };

    private static bool Same(float a, float b) => MathF.Abs(Angles.Wrap(a - b)) <= Tolerance;
}
