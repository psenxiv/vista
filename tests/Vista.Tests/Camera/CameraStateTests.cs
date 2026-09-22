using System.Numerics;
using Vista.Core.Camera;
using Xunit;

namespace Vista.Tests.Camera;

public class CameraStateTests
{
    [Fact]
    public void CameraState_CarriesPositionLookAtAndFov()
    {
        var state = new CameraState(
            Position: new Vector3(1, 2, 3),
            LookAt: new Vector3(4, 5, 6),
            Fov: 1.2f);

        Assert.Equal(new Vector3(1, 2, 3), state.Position);
        Assert.Equal(new Vector3(4, 5, 6), state.LookAt);
        Assert.Equal(1.2f, state.Fov);
    }

    [Fact]
    public void CameraState_EqualityIsByValue()
    {
        var a = new CameraState(Vector3.One, Vector3.Zero, 1f);
        var b = new CameraState(Vector3.One, Vector3.Zero, 1f);

        Assert.Equal(a, b);
    }
}
