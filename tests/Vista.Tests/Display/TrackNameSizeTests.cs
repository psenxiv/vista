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
}
