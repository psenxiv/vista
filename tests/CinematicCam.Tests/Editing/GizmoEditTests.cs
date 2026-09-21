using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class GizmoEditTests
{
    private static readonly ControlPoint Original = new(new Vector3(1f, 2f, 3f), 0.7f, 0.3f, 0.9f, 0.4f);

    private static Matrix4x4 Pose(Vector3 position, float yaw, float pitch, float roll) => PoseMatrix.From(position, yaw, pitch, roll);

    [Theory]
    [InlineData(GizmoMode.Move, AimMode.AimKeys)]
    [InlineData(GizmoMode.Rotate, AimMode.AimKeys)]
    [InlineData(GizmoMode.Rotate, AimMode.PathTangent)]
    public void AnUnmovedGizmoReturnsTheOriginal(GizmoMode mode, AimMode aim)
    {
        var dragged = Pose(Original.Position, Original.Yaw, Original.Pitch, Original.Roll);
        Assert.Same(Original, GizmoEdit.Apply(Original, dragged, mode, aim));
    }

    [Fact]
    public void AYawThatWrappedAFullTurnCountsAsUnchanged()
    {
        var turned = Original with { Yaw = Original.Yaw + MathF.Tau };
        var dragged = Pose(turned.Position, Original.Yaw, Original.Pitch, Original.Roll);
        Assert.Same(turned, GizmoEdit.Apply(turned, dragged, GizmoMode.Rotate, AimMode.AimKeys));
    }

    [Fact]
    public void MoveTakesOnlyThePosition()
    {
        var dragged = Pose(new Vector3(5f, 6f, 7f), Original.Yaw + 0.2f, Original.Pitch, Original.Roll);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Move, AimMode.AimKeys);
        Assert.Equal(Original with { Position = new Vector3(5f, 6f, 7f) }, edited);
    }

    [Fact]
    public void RotateWithRecordedAimTakesYawPitchAndRollButNotPosition()
    {
        var dragged = Pose(new Vector3(9f, 9f, 9f), 1.2f, -0.4f, 0.1f);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Rotate, AimMode.AimKeys);
        Assert.Equal(Original.Position, edited.Position);
        Assert.Equal(1.2f, edited.Yaw, 4);
        Assert.Equal(-0.4f, edited.Pitch, 4);
        Assert.Equal(0.1f, edited.Roll, 4);
        Assert.Equal(Original.Fov, edited.Fov);
    }

    [Fact]
    public void RotateWithDirectionOfTravelTakesOnlyRoll()
    {
        var dragged = Pose(Original.Position, Original.Yaw, Original.Pitch, Original.Roll + 0.5f);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Rotate, AimMode.PathTangent);
        Assert.Equal(Original.Yaw, edited.Yaw);
        Assert.Equal(Original.Pitch, edited.Pitch);
        Assert.Equal(Original.Roll + 0.5f, edited.Roll, 4);
    }
}
