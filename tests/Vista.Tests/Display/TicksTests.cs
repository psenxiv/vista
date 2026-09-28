using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class TicksTests
{
    [Fact]
    public void TickStepsAreRound()
    {
        // 60 over 6 is 10 exactly; 15.3 over 16 is 0.96, rounding up to 1; 2 over 16 is 0.125, up to 0.2.
        Assert.Equal(10f, Ticks.Step(60f, 6), 1e-5f);
        Assert.Equal(1f, Ticks.Step(15.3f, 16), 1e-5f);
        Assert.Equal(0.2f, Ticks.Step(2f, 16), 1e-5f);
    }

    // Whole seconds need no decimals, tenths one, anything finer two.

    [Fact]
    public void TickLabelsShowAsManyDecimalsAsTheStep()
    {
        Assert.Equal("0", Ticks.Format(2f));
        Assert.Equal("0", Ticks.Format(1f));
        Assert.Equal("0.0", Ticks.Format(0.5f));
        Assert.Equal("0.0", Ticks.Format(0.1f));
        Assert.Equal("0.00", Ticks.Format(0.05f));
    }
}
