using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
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
    public void AnUnturnedRingReturnsTheOriginal(GimbalRing ring)
        => Assert.Same(Original, GizmoEdit.Rotate(Original, ring, GizmoEdit.RingFrame(Original, ring)));

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
    public void ThePitchRingStopsAtThePitchLimit()
    {
        // pitch + phi, so +2 rad takes 0.3 to 2.3 and -2 rad takes it to -1.7; each clamps to its own end.
        var up = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(2f));
        var down = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(-2f));

        Assert.Equal(TrackAim.PitchLimit, GizmoEdit.Rotate(Original, GimbalRing.Pitch, up).Pitch, 4);
        Assert.Equal(-TrackAim.PitchLimit, GizmoEdit.Rotate(Original, GimbalRing.Pitch, down).Pitch, 4);
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
