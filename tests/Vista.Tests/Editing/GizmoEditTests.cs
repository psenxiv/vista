using System.Numerics;
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
        Assert.Equal(0.3f, MathF.Abs(Delta(edited.Yaw, Original.Yaw)), 4);
        Assert.Equal(Original with { Yaw = edited.Yaw }, edited);
    }

    [Fact]
    public void ThePitchRingChangesOnlyPitch()
    {
        var dragged = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(0.2f));
        var edited = GizmoEdit.Rotate(Original, GimbalRing.Pitch, dragged);
        Assert.Equal(0.2f, MathF.Abs(edited.Pitch - Original.Pitch), 4);
        Assert.Equal(Original with { Pitch = edited.Pitch }, edited);
    }

    [Fact]
    public void ThePitchRingStopsAtThePitchLimit()
    {
        var up = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(2f));
        var down = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Pitch), Matrix4x4.CreateRotationX(-2f));
        var pitches = new[] { GizmoEdit.Rotate(Original, GimbalRing.Pitch, up).Pitch, GizmoEdit.Rotate(Original, GimbalRing.Pitch, down).Pitch };
        Assert.Contains(pitches, p => MathF.Abs(p - TrackAim.PitchLimit) < 1e-4f);
        Assert.Contains(pitches, p => MathF.Abs(p + TrackAim.PitchLimit) < 1e-4f);
    }

    [Fact]
    public void TheRollRingChangesOnlyRoll()
    {
        var dragged = Turn(GizmoEdit.RingFrame(Original, GimbalRing.Roll), Matrix4x4.CreateRotationZ(0.5f));
        var edited = GizmoEdit.Rotate(Original, GimbalRing.Roll, dragged);
        Assert.Equal(0.5f, MathF.Abs(Delta(edited.Roll, Original.Roll)), 4);
        Assert.Equal(Original with { Roll = edited.Roll }, edited);
    }

    [Fact]
    public void AYawThatWrappedAFullTurnCountsAsUnchanged()
    {
        var turned = Original with { Yaw = Original.Yaw + MathF.Tau };
        Assert.Same(turned, GizmoEdit.Rotate(turned, GimbalRing.Yaw, GizmoEdit.RingFrame(Original, GimbalRing.Yaw)));
    }
}
