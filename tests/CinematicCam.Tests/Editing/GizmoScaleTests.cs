using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class GizmoScaleTests
{
    private static Matrix4x4 ViewProjection()
        => Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 10f), Vector3.Zero, Vector3.UnitY)
         * Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);

    [Fact]
    public void MatchingMatricesNeedNoCorrection()
        => Assert.Equal(0.1f, GizmoScale.ClipSize(Vector3.Zero, Vector3.UnitX, ViewProjection(), ViewProjection(), 0.1f), 5);

    [Fact]
    public void AGizmoProjectionTwiceTheSizeOnScreenGetsTwiceTheClipSize()
    {
        var doubled = ViewProjection() * Matrix4x4.CreateScale(2f, 2f, 1f);
        Assert.Equal(0.2f, GizmoScale.ClipSize(Vector3.Zero, Vector3.UnitX, doubled, ViewProjection(), 0.1f), 4);
    }

    [Fact]
    public void APointBehindTheCameraFallsBackToTheTarget()
        => Assert.Equal(0.1f, GizmoScale.ClipSize(new Vector3(0f, 0f, 20f), Vector3.UnitX, ViewProjection(), Matrix4x4.Identity with { M44 = 0f }, 0.1f));
}
