using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Editing;

public class GizmoEditTests
{
    private static readonly ControlPoint Original = new(new Vector3(1f, 2f, 3f), 0.7f, 0.3f, 0.9f, 0.4f);

    // ImGuizmo's local rotation: turn the frame about one of its own axes, keeping its origin.
    private static Matrix4x4 Turn(Matrix4x4 frame, Matrix4x4 localRotation) => localRotation * frame;

    private static float Delta(float a, float b) => MathF.IEEERemainder(a - b, MathF.Tau);

    [Fact]
    public void AnUnmovedMoveReturnsTheOriginal()
    {
        var frame = PoseMatrix.From(Original.Position, Original.Yaw, Original.Pitch, Original.Roll);
        Assert.Same(Original, GizmoEdit.Move(Original, frame));
    }

    [Fact]
    public void MoveTakesOnlyThePosition()
    {
        var dragged = PoseMatrix.From(new Vector3(5f, 6f, 7f), Original.Yaw + 0.2f, Original.Pitch, Original.Roll);
        Assert.Equal(Original with { Position = new Vector3(5f, 6f, 7f) }, GizmoEdit.Move(Original, dragged));
    }

    [Theory]
    [InlineData(GimbalRing.Yaw)]
    [InlineData(GimbalRing.Pitch)]
    [InlineData(GimbalRing.Roll)]
    public void AnUnturnedRingReturnsTheOriginal(GimbalRing ring) =>
        Assert.Same(Original, GizmoEdit.Rotate(Original, ring, GizmoEdit.RingFrame(Original, ring)));

    [Fact]
    public void TheYawRingLiesFlatAroundWorldUp()
    {
        var frame = GizmoEdit.RingFrame(Original, GimbalRing.Yaw);
        Assert.Equal(1f, frame.M22, 5);
    }

    [Fact]
    public void TheYawRingChangesOnlyYaw()
    {
        var dragged = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Yaw), Matrix4x4.CreateRotationY(0.3f));
        var edited = GizmoEdit.Rotate(Original, GimbalRing.Yaw, dragged);
        // Ry(t) * frame turns forward about up by +t, so the yaw rises by the drag angle.
        Assert.Equal(0.3f, Delta(edited.Yaw, Original.Yaw), 4);
        Assert.Equal(Original with { Yaw = edited.Yaw }, edited);
    }

    [Fact]
    public void ThePitchRingChangesOnlyPitch()
    {
        var dragged = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(0.2f));
        var edited = GizmoEdit.Rotate(Original, GimbalRing.Pitch, dragged);
        // Rx(p) * frame leaves row 3 as the back vector for pitch + p, so the pitch rises by the drag angle.
        Assert.Equal(0.2f, edited.Pitch - Original.Pitch, 4);
        Assert.Equal(Original with { Pitch = edited.Pitch }, edited);
    }

    [Fact]
    public void ThePitchRingGoesOverTheTopPastVertical()
    {
        var original = new ControlPoint(new Vector3(4f, -1f, 2f), 0f, 80f * MathF.PI / 180f, 0.4f);
        var dragged = Turn(
            GizmoEdit.RingFrame(original, GimbalRing.Pitch),
            Matrix4x4.CreateRotationX(20f * MathF.PI / 180f)
        );
        var edited = GizmoEdit.Rotate(original, GimbalRing.Pitch, dragged);

        // 80 deg + 20 deg = 100 deg, past vertical: ToAngles normalises it to the mirror image,
        // pitch back at 80 deg with yaw and roll each turned by a half turn.
        Assert.Equal(80f * MathF.PI / 180f, edited.Pitch, 4);
        Assert.Equal(MathF.PI, MathF.Abs(Delta(edited.Yaw, original.Yaw)), 3);
        Assert.Equal(MathF.PI, MathF.Abs(Delta(edited.Roll, original.Roll)), 3);

        // The picture itself is unchanged: the edited angles' forward matches the dragged matrix's forward.
        var draggedForward = -new Vector3(dragged.M31, dragged.M32, dragged.M33);
        var editedForward = CameraRotation.Forward(CameraRotation.FromAngles(edited.Yaw, edited.Pitch, edited.Roll));
        Assert.True(Vector3.Distance(draggedForward, editedForward) < 1e-4f);
    }

    [Fact]
    public void TheRollRingChangesOnlyRoll()
    {
        var dragged = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Roll), Matrix4x4.CreateRotationZ(0.5f));
        var edited = GizmoEdit.Rotate(Original, GimbalRing.Roll, dragged);
        // Row 3 is back, not forward, so a +psi turn about the frame's own Z is a -psi turn
        // about the view direction, which is what ToPose measures the roll against.
        Assert.Equal(-0.5f, Delta(edited.Roll, Original.Roll), 4);
        Assert.Equal(Original with { Roll = edited.Roll }, edited);
    }

    [Fact]
    public void AYawThatWrappedAFullTurnCountsAsUnchanged()
    {
        var turned = Original with { Yaw = Original.Yaw + MathF.Tau };
        Assert.Same(turned, GizmoEdit.Rotate(turned, GimbalRing.Yaw, GizmoEdit.RingFrame(Original, GimbalRing.Yaw)));
    }
}
