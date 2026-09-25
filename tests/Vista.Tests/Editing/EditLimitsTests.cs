using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Editing;

public class EditLimitsTests
{
    // Each limit is given 0.7 as the current value, which a value that isn't finite leaves unchanged.

    [Fact]
    public void PitchClampsToAQuarterTurn()
    {
        // pi / 2 radians: straight up or down, with no 89 degree cap.
        Assert.Equal(1.5707964f, EditLimits.Pitch(2f, 0.7f));
        Assert.Equal(-1.5707964f, EditLimits.Pitch(-2f, 0.7f));
        Assert.Equal(0.3f, EditLimits.Pitch(0.3f, 0.7f));
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(MathF.PI + 0.5f, -MathF.PI + 0.5f)]
    [InlineData(-MathF.PI - 0.5f, MathF.PI - 0.5f)]
    public void AnglesWrapToAHalfTurnEitherWay(float input, float expected) =>
        Assert.Equal(expected, EditLimits.Angle(input, 0.7f), 4);

    [Fact]
    public void FovClampsToFiveToOneHundredAndTwentyDegrees()
    {
        Assert.Equal(5f * Deg, EditLimits.Fov(1f * Deg, 0.7f), 5);
        Assert.Equal(120f * Deg, EditLimits.Fov(200f * Deg, 0.7f), 5);
        Assert.Equal(90f * Deg, EditLimits.Fov(90f * Deg, 0.7f), 5);
    }

    [Fact]
    public void AFieldOfViewTypedAtALimitLandsExactlyOnIt()
    {
        // 5 × 0.017453292 (π/180 in single precision) is 0.08726646; typed degrees convert by the same constant.
        Assert.Equal(0.08726646f, EditLimits.MinFov);
        Assert.Equal(EditLimits.MinFov, Angles.Radians(5f));
        Assert.Equal(EditLimits.MaxFov, Angles.Radians(120f));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void AValueThatIsNotFiniteKeepsTheCurrentOne(float value)
    {
        Assert.Equal(0.7f, EditLimits.Pitch(value, 0.7f));
        Assert.Equal(0.7f, EditLimits.Angle(value, 0.7f));
        Assert.Equal(0.7f, EditLimits.Fov(value, 0.7f));
    }

    // Axis 0, 1 and 2 are X, Y and Z; a value that isn't finite keeps the coordinate it had.

    [Fact]
    public void ACoordinateSetsOneAxisAndKeepsItWhenNotFinite()
    {
        var position = new Vector3(1f, 2f, 3f);

        Assert.Equal(new Vector3(-137.1f, 2f, 3f), EditLimits.Coordinate(position, 0, -137.1f));
        Assert.Equal(position, EditLimits.Coordinate(position, 1, float.PositiveInfinity));
        Assert.Equal(new Vector3(1f, 2f, 5f), EditLimits.Coordinate(position, 2, 5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => EditLimits.Coordinate(position, 3, 5f));
    }
}
