using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class EdgeScrollTests
{
    // A list from y = 100 to 400 with rows 20 high: the scrolling zones are 100 to 120 and 380 to 400.
    [Theory]
    [InlineData(250f, 0f)]
    [InlineData(120f, 0f)]
    [InlineData(110f, -5f)]
    [InlineData(100f, -10f)]
    [InlineData(60f, -10f)]
    [InlineData(380f, 0f)]
    [InlineData(390f, 5f)]
    [InlineData(400f, 10f)]
    [InlineData(450f, 10f)]
    public void ItScrollsFasterTheCloserTheMouseIsToAnEdge(float mouseY, float rowsPerSecond) =>
        Assert.Equal(rowsPerSecond, EdgeScroll.RowsPerSecond(mouseY, 100f, 400f, 20f), 1e-4f);

    [Fact]
    public void AShortListKeepsAMiddleThatDoesntScroll()
    {
        // 30 high with rows of 20: the zones shrink to a third, 10 each, so halfway into the top one (5 down) it scrolls up
        // at half speed and the middle, 15 down, doesn't scroll.
        Assert.Equal(-5f, EdgeScroll.RowsPerSecond(5f, 0f, 30f, 20f), 1e-4f);
        Assert.Equal(0f, EdgeScroll.RowsPerSecond(15f, 0f, 30f, 20f), 1e-4f);
    }

    [Fact]
    public void AListWithNoHeightDoesntScroll() => Assert.Equal(0f, EdgeScroll.RowsPerSecond(0f, 0f, 0f, 20f));
}
