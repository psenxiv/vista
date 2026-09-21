using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

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
    public void FovClampsToTheGamesRange()
    {
        Assert.Equal(0.5f, EditLimits.Fov(0.2f, 0.5f, 1.2f));
        Assert.Equal(1.2f, EditLimits.Fov(2f, 0.5f, 1.2f));
        Assert.Equal(0.8f, EditLimits.Fov(0.8f, 0.5f, 1.2f));
        Assert.Equal(0.5f, EditLimits.Fov(float.NaN, 0.5f, 1.2f));
    }

    [Fact]
    public void CoordinatesKeepTheCurrentValueWhenNotFinite()
    {
        Assert.Equal(-137.1f, EditLimits.Coordinate(-137.1f, 3f));
        Assert.Equal(3f, EditLimits.Coordinate(float.PositiveInfinity, 3f));
    }
}
