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
}
