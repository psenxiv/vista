using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class PanelWidthTests
{
    [Theory]
    [InlineData(300f, 300f)]
    [InlineData(100f, 240f)]
    [InlineData(0f, 240f)]
    [InlineData(800f, 600f)]
    [InlineData(float.NaN, 240f)]
    public void AWidthIsHeldBetween240And600(float width, float expected)
    {
        Assert.Equal(expected, PanelWidth.Clamp(width));
    }
}
