using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class FractionTests
{
    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1.5f, 1f)]
    public void ClampKeepsAValueWithinZeroToOne(float value, float expected) =>
        Assert.Equal(expected, Fraction.Clamp(value));

    [Theory]
    // 5 is a quarter of the way from 2 to 14; −10 and 20 lie outside, so they clamp to the ends.
    [InlineData(5f, 0.25f)]
    [InlineData(-10f, 0f)]
    [InlineData(20f, 1f)]
    public void BetweenIsHowFarAValueIsAlongASpan(float value, float expected) =>
        Assert.Equal(expected, Fraction.Between(value, 2f, 14f, 0.5f), 1e-6f);

    [Fact]
    public void BetweenGivesTheEmptyAnswerWhenTheSpanHasNoLength()
    {
        Assert.Equal(0.5f, Fraction.Between(3f, 2f, 2f, 0.5f));
        Assert.Equal(0f, Fraction.Between(3f, 4f, 2f, 0f));
    }
}
