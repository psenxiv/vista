using System.Numerics;
using Vista.Core.Camera;
using Xunit;

namespace Vista.Tests.Camera;

public class CameraStateTests
{
    [Fact]
    public void ForwardIsTheUnitWayToTheLookAt() =>
        // From (1, 2, 3) to (1, 2, −7) is 10 along −z.
        Assert.Equal(
            new Vector3(0f, 0f, -1f),
            new CameraState(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, -7f), Vector3.UnitY, 1f).Forward
        );
}
