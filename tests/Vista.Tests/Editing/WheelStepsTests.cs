using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class WheelStepsTests
{
    // Quarters and halves are exact in float, so each carry below is exact.

    [Fact]
    public void TravelAddsUpToWholeNotchesAndKeepsTheRest()
    {
        var wheel = new WheelSteps();

        Assert.Equal(0, wheel.Take(0.25f));
        // 0.25 + 0.5 = 0.75: still under a notch.
        Assert.Equal(0, wheel.Take(0.5f));
        // 0.75 + 0.5 = 1.25: one notch, 0.25 kept.
        Assert.Equal(1, wheel.Take(0.5f));
        // 0.25 − 1.75 = −1.5: one notch down, truncating towards zero, −0.5 kept.
        Assert.Equal(-1, wheel.Take(-1.75f));
        // −0.5 + 3.5 = 3: three notches at once.
        Assert.Equal(3, wheel.Take(3.5f));
    }

    [Fact]
    public void ResetDropsTravelNotYetTaken()
    {
        var wheel = new WheelSteps();
        wheel.Take(0.5f);

        wheel.Reset();

        // 0.75 alone is under a notch; with the dropped 0.5 it would have been one.
        Assert.Equal(0, wheel.Take(0.75f));
    }
}
