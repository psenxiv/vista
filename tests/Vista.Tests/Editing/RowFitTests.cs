using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class RowFitTests
{
    // Every character, the ellipsis included, measures 10 px.
    private static float Measure(string text) => text.Length * 10f;

    [Fact]
    public void ANameThatFitsIsLeftWhole()
    {
        Assert.Equal("Track", RowFit.Ellipsis("Track", 50f, Measure));
    }

    [Fact]
    public void ALongNameIsCutToTheLongestStartThatFitsWithTheEllipsis()
    {
        // 100 px holds ten characters: nine of the name and the ellipsis.
        Assert.Equal("Establish…", RowFit.Ellipsis("Establishing shot", 100f, Measure));
    }

    [Fact]
    public void ACutDropsTheSpacesBeforeTheEllipsis()
    {
        // 40 px holds "Ab c…" less one character, so the cut falls after "Ab ", whose space goes.
        Assert.Equal("Ab…", RowFit.Ellipsis("Ab cdefgh", 40f, Measure));
    }

    [Fact]
    public void ACutNeverSplitsASurrogatePair()
    {
        // The emoji is two UTF-16 characters; 40 px would take "ab", its first half and the ellipsis.
        Assert.Equal("ab…", RowFit.Ellipsis("ab😀cd", 40f, Measure));
    }

    [Theory]
    [InlineData(15f, "…")]
    [InlineData(5f, "")]
    public void TooNarrowForAnyOfTheNameLeavesTheEllipsisOrNothing(float width, string expected)
    {
        Assert.Equal(expected, RowFit.Ellipsis("Hello", width, Measure));
    }

    [Theory]
    // 60 px over at 30 px/s is 2 s each way; with 1 s rests the cycle is 6 s.
    [InlineData(0.5, 0f)]
    [InlineData(2.0, 30f)]
    [InlineData(3.0, 60f)]
    [InlineData(3.5, 60f)]
    [InlineData(5.0, 30f)]
    [InlineData(6.5, 0f)]
    [InlineData(8.0, 30f)]
    public void AnOverflowingNameRestsScrollsToItsEndRestsAndScrollsBack(double seconds, float expected)
    {
        Assert.Equal(expected, RowFit.Scroll(60f, seconds), 1e-4f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-12f)]
    public void ANameThatFitsNeverScrolls(float overflow)
    {
        Assert.Equal(0f, RowFit.Scroll(overflow, 2.0));
    }
}
