using Vista.Core.Camera;
using Xunit;

namespace Vista.Tests.Camera;

public class AnglesTests
{
    // π radians is half a turn, 180°; 90° is a quarter turn, π/2.

    [Fact]
    public void DegreesAndRadiansConvertByHalfATurn()
    {
        Assert.Equal(180f, Angles.Degrees(MathF.PI), 1e-4f);
        Assert.Equal(MathF.PI / 2f, Angles.Radians(90f), 1e-6f);
    }

    [Fact]
    public void DegreesUndoRadians() => Assert.Equal(37f, Angles.Degrees(Angles.Radians(37f)), 1e-4f);
}
