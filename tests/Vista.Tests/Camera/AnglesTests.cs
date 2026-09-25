using Vista.Core.Camera;
using Xunit;
using static Vista.Tests.Fixtures;

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

    [Fact]
    public void UnwrapTakesTheShortWayAcrossPlusMinus180()
    {
        var unwrapped = Angles.Unwrap([170f * Deg, -170f * Deg]);

        // −170° is 20° on from 170° the short way round: 190°.
        Assert.Equal(170f * Deg, unwrapped[0], 4);
        Assert.Equal(190f * Deg, unwrapped[1], 4);
    }

    [Fact]
    public void UnwrapLeavesASmallStepUntouched()
    {
        var unwrapped = Angles.Unwrap([10f * Deg, 15f * Deg, 5f * Deg]);

        Assert.Equal(10f * Deg, unwrapped[0], 4);
        Assert.Equal(15f * Deg, unwrapped[1], 4);
        Assert.Equal(5f * Deg, unwrapped[2], 4);
    }

    [Fact]
    public void UnwrapOfNothingIsEmpty() => Assert.Empty(Angles.Unwrap([]));
}
