using System.Numerics;
using CinematicCam.Core.Camera;
using Xunit;

namespace CinematicCam.Tests.Camera;

public class ScreenProjectionTests
{
    private static readonly Vector2 Viewport = new(1920f, 1080f);

    // Camera at z = 10 looking at the origin.
    private static Matrix4x4 ViewProjection()
        => Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 10f), Vector3.Zero, Vector3.UnitY)
         * Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);

    [Fact]
    public void APointStraightAheadLandsInTheCentre()
    {
        var screen = ScreenProjection.Project(Vector3.Zero, ViewProjection(), Viewport);
        Assert.NotNull(screen);
        Assert.Equal(960f, screen!.Value.X, 2);
        Assert.Equal(540f, screen.Value.Y, 2);
    }

    [Fact]
    public void RightIsRightAndUpIsUpOnScreen()
    {
        var right = ScreenProjection.Project(Vector3.UnitX, ViewProjection(), Viewport)!.Value;
        var up = ScreenProjection.Project(Vector3.UnitY, ViewProjection(), Viewport)!.Value;
        Assert.True(right.X > 960f);
        Assert.True(up.Y < 540f);
    }

    [Fact]
    public void APointBehindTheCameraIsNull()
        => Assert.Null(ScreenProjection.Project(new Vector3(0f, 0f, 20f), ViewProjection(), Viewport));

    [Fact]
    public void ASegmentInFrontProjectsToItsEndPoints()
    {
        var a = new Vector3(-1f, 0f, 0f);
        var b = new Vector3(1f, 1f, 0f);
        var segment = ScreenProjection.ProjectSegment(a, b, ViewProjection(), Viewport, 0.1f);
        Assert.NotNull(segment);
        Assert.Equal(ScreenProjection.Project(a, ViewProjection(), Viewport)!.Value, segment!.Value.Start);
        Assert.Equal(ScreenProjection.Project(b, ViewProjection(), Viewport)!.Value, segment.Value.End);
    }

    [Fact]
    public void ASegmentBehindTheCameraIsSkipped()
        => Assert.Null(ScreenProjection.ProjectSegment(new Vector3(0f, 0f, 20f), new Vector3(1f, 0f, 30f), ViewProjection(), Viewport, 0.1f));

    [Fact]
    public void ASegmentCrossingTheNearPlaneIsCutWhereItCrosses()
    {
        // From 10 in front of the camera to 10 behind it, off to one side.
        var front = new Vector3(1f, 0f, 0f);
        var behind = new Vector3(1f, 0f, 20f);
        var segment = ScreenProjection.ProjectSegment(front, behind, ViewProjection(), Viewport, 0.1f)!.Value;

        var cut = ScreenProjection.Project(new Vector3(1f, 0f, 9.9f), ViewProjection(), Viewport)!.Value;
        Assert.True(Vector2.Distance(cut, segment.End) < 1f);

        var reversed = ScreenProjection.ProjectSegment(behind, front, ViewProjection(), Viewport, 0.1f)!.Value;
        Assert.True(Vector2.Distance(cut, reversed.Start) < 1f);
    }
}
