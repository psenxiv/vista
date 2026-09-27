using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class TrackNameSizeTests
{
    [Fact]
    public void NamesStartAtOneAndAHalfTimesTheFontSize() => Assert.Equal(1.5f, TrackNameSize.Default);

    [Theory]
    [InlineData(2f, 2f)]
    [InlineData(0.75f, 0.75f)]
    [InlineData(3f, 3f)]
    [InlineData(0.5f, 0.75f)]
    [InlineData(-1f, 0.75f)]
    [InlineData(4f, 3f)]
    [InlineData(float.NaN, 1.5f)]
    [InlineData(float.PositiveInfinity, 1.5f)]
    [InlineData(float.NegativeInfinity, 1.5f)]
    public void ASizeIsHeldBetweenHalfAndDoubleTheDefault(float size, float expected)
    {
        // Half of 1.5 is 0.75 and double is 3; a size that isn't a number goes back to 1.5.
        Assert.Equal(expected, TrackNameSize.Clamp(size));
    }

    [Theory]
    [InlineData(1.5f, 100f)]
    [InlineData(0.75f, 50f)]
    [InlineData(3f, 200f)]
    [InlineData(2.25f, 150f)]
    public void TheSliderShowsASizeAsAPercentageOfTheDefault(float size, float expected)
    {
        // 1.5 is 100% of itself, 0.75 is half of it, 3 is double, and 2.25 is one and a half times.
        Assert.Equal(expected, TrackNameSize.Percent(size), 1e-4f);
    }

    [Theory]
    [InlineData(100f, 1.5f)]
    [InlineData(50f, 0.75f)]
    [InlineData(200f, 3f)]
    [InlineData(150f, 2.25f)]
    [InlineData(20f, 0.75f)]
    [InlineData(400f, 3f)]
    [InlineData(float.NaN, 1.5f)]
    public void APercentageGivesASizeWithinRange(float percent, float expected)
    {
        // 100% of 1.5 is 1.5, 50% is 0.75, 200% is 3 and 150% is 2.25; 20% (0.3) and 400% (6) are held at 0.75 and 3, and not a number gives 1.5.
        Assert.Equal(expected, TrackNameSize.FromPercent(percent), 1e-4f);
    }
}
