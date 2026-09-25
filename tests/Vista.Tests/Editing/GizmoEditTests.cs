using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Editing;

public class GizmoEditTests
{
    private static readonly ControlPoint Original = new(new Vector3(1f, 2f, 3f), 0.7f, 0.3f, 0.9f, 0.4f);

    // ImGuizmo's local rotation: turn the frame about one of its own axes, keeping its origin.
    private static Matrix4x4 Turn(Matrix4x4 frame, Matrix4x4 localRotation) => localRotation * frame;

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
    public void AScaledPitchRingFrameReadsAsUnturned()
    {
        // Doubling the frame's axes changes their length, not their direction, so nothing turned.
        var dragged = Matrix4x4.CreateScale(2f) * GizmoEdit.RingFrame(Original, GimbalRing.Pitch);
        Assert.Same(Original, GizmoEdit.Rotate(Original, GimbalRing.Pitch, dragged));
    }

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
        Assert.Equal(0.3f, Angles.Delta(Original.Yaw, edited.Yaw), 4);
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
        var original = new ControlPoint(new Vector3(4f, -1f, 2f), 0f, 80f * Deg, 0.4f);
        var dragged = Turn(GizmoEdit.RingFrame(original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(20f * Deg));
        var edited = GizmoEdit.Rotate(original, GimbalRing.Pitch, dragged);

        // 80 deg + 20 deg = 100 deg, past vertical: ToAngles normalises it to the mirror image,
        // pitch back at 80 deg with yaw and roll each turned by a half turn.
        Assert.Equal(80f * Deg, edited.Pitch, 4);
        Assert.Equal(MathF.PI, MathF.Abs(Angles.Delta(original.Yaw, edited.Yaw)), 3);
        Assert.Equal(MathF.PI, MathF.Abs(Angles.Delta(original.Roll, edited.Roll)), 3);

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
        Assert.Equal(-0.5f, Angles.Delta(Original.Roll, edited.Roll), 4);
        Assert.Equal(Original with { Roll = edited.Roll }, edited);
    }

    [Fact]
    public void AYawThatWrappedAFullTurnCountsAsUnchanged()
    {
        var turned = Original with { Yaw = Original.Yaw + MathF.Tau };
        Assert.Same(turned, GizmoEdit.Rotate(turned, GimbalRing.Yaw, GizmoEdit.RingFrame(Original, GimbalRing.Yaw)));
    }

    // CreateRotationY(0.5)'s third row is (sin 0.5, 0, cos 0.5), so the forward is (−sin, 0, −cos) and its yaw atan2(sin, cos) = 0.5.

    [Fact]
    public void AnAnchorTurnsToTheDraggedYawOrMovesToTheDraggedPosition()
    {
        var start = new Anchor(new Vector3(1f, 2f, 3f), 0.2f);

        var turned = GizmoEdit.MoveAnchor(start, Matrix4x4.CreateRotationY(0.5f), rotate: true);
        Assert.Equal(0.5f, turned.Yaw, 1e-6f);
        Assert.Equal(start.Position, turned.Position);

        var moved = GizmoEdit.MoveAnchor(start, Matrix4x4.CreateTranslation(4f, 5f, 6f), rotate: false);
        Assert.Equal(new Vector3(4f, 5f, 6f), moved.Position);
        Assert.Equal(0.2f, moved.Yaw);
    }
}
