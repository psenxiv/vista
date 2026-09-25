using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class ControlPointTests
{
    // Facing −x: yaw atan2(−f.x, −f.z) = atan2(1, 0) = π/2, pitch 0; up +y is the upright up there, so roll 0.

    [Fact]
    public void AFrameBecomesAPointFacingItsWay()
    {
        var at = new Vector3(1f, 2f, 3f);

        var point = ControlPoint.FromFrame(new CameraState(at, at - Vector3.UnitX, Vector3.UnitY, 0.8f));

        Assert.Equal(at, point.Position);
        Assert.Equal(MathF.PI / 2f, point.Yaw, 1e-5f);
        Assert.Equal(0f, point.Pitch, 1e-5f);
        Assert.Equal(0f, point.Roll, 1e-5f);
        Assert.Equal(0.8f, point.Fov);
    }

    // Facing −z with up +x: the upright up is +y, cross(+y, +x) = (0, 0, −1), which dots 1 with the forward, and dot(+y, +x) = 0, so roll atan2(1, 0) = π/2.

    [Fact]
    public void AFrameRolledOntoItsSideKeepsItsRoll()
    {
        var point = ControlPoint.FromFrame(new CameraState(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitX, 1f));

        Assert.Equal(0f, point.Yaw, 1e-5f);
        Assert.Equal(MathF.PI / 2f, point.Roll, 1e-5f);
    }

    [Fact]
    public void ARotationBecomesThePointItWasBuiltFrom()
    {
        var point = ControlPoint.FromRotation(Vector3.One, CameraRotation.FromAngles(0.3f, -0.2f, 0.1f), 1f);

        Assert.Equal(0.3f, point.Yaw, 1e-5f);
        Assert.Equal(-0.2f, point.Pitch, 1e-5f);
        Assert.Equal(0.1f, point.Roll, 1e-5f);
        Assert.Equal(Vector3.One, point.Position);
    }
}
