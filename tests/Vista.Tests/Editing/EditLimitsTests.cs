using System.Numerics;
using Vista.Core.Editing;
using Xunit;

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
        const float degree = MathF.PI / 180f;
        Assert.Equal(5f * degree, EditLimits.Fov(1f * degree, 0.7f), 5);
        Assert.Equal(120f * degree, EditLimits.Fov(200f * degree, 0.7f), 5);
        Assert.Equal(90f * degree, EditLimits.Fov(90f * degree, 0.7f), 5);
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
