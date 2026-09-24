using Vista.Core.Editing;
using Vista.Core.Tracks.Aiming;
using Xunit;

namespace Vista.Tests.Editing;

public class EditLimitsTests
{
    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(-3f, 0.1f)]
    [InlineData(5f, 5f)]
    [InlineData(9999f, 600f)]
    [InlineData(float.NaN, 0.1f)]
    public void LegClampsToATenthOfASecondAndTenMinutes(float input, float expected)
        => Assert.Equal(expected, EditLimits.Leg(input));

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(9999f, 600f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void HoldClampsToZeroAndTenMinutes(float input, float expected)
        => Assert.Equal(expected, EditLimits.Hold(input));

    [Theory]
    [InlineData(0f, 0.01f)]
    [InlineData(-3f, 0.01f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(9999f, 100f)]
    [InlineData(float.NaN, 5f)]
    public void SpeedClampsToAHundredthAndAHundredYalmsPerSecond(float input, float expected)
        => Assert.Equal(expected, EditLimits.Speed(input));

    [Theory]
    [InlineData(0f, 0.2f)]
    [InlineData(-3f, 0.2f)]
    [InlineData(30f, 30f)]
    [InlineData(99999f, 3600f)]
    [InlineData(float.NaN, 0.2f)]
    public void ShotDurationClampsToTwoTenthsOfASecondAndAnHour(float input, float expected)
        => Assert.Equal(expected, EditLimits.ShotDuration(input));

    [Fact]
    public void PitchClampsToTheGizmoLimit()
    {
        Assert.Equal(TrackAim.PitchLimit, EditLimits.Pitch(2f));
        Assert.Equal(-TrackAim.PitchLimit, EditLimits.Pitch(-2f));
        Assert.Equal(0.3f, EditLimits.Pitch(0.3f));
        Assert.Equal(0f, EditLimits.Pitch(float.NaN));
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(MathF.PI + 0.5f, -MathF.PI + 0.5f)]
    [InlineData(-MathF.PI - 0.5f, MathF.PI - 0.5f)]
    [InlineData(float.NaN, 0f)]
    public void AnglesWrapToAHalfTurnEitherWay(float input, float expected)
        => Assert.Equal(expected, EditLimits.Angle(input), 4);

    [Fact]
    public void FovClampsToFiveToOneHundredAndTwentyDegrees()
    {
        const float degree = MathF.PI / 180f;
        Assert.Equal(5f * degree, EditLimits.Fov(1f * degree), 5);
        Assert.Equal(120f * degree, EditLimits.Fov(200f * degree), 5);
        Assert.Equal(90f * degree, EditLimits.Fov(90f * degree), 5);
        Assert.Equal(5f * degree, EditLimits.Fov(float.NaN), 5);
    }

    [Fact]
    public void CoordinatesKeepTheCurrentValueWhenNotFinite()
    {
        Assert.Equal(-137.1f, EditLimits.Coordinate(-137.1f, 3f));
        Assert.Equal(3f, EditLimits.Coordinate(float.PositiveInfinity, 3f));
    }
}
