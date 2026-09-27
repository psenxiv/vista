using System.Globalization;
using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class UnitsTests
{
    [Fact]
    public void EachQuantityShowsTwoDecimalsAndItsUnit()
    {
        Assert.Equal("1.50 s", Units.Seconds(1.5));
        Assert.Equal("1.50", Units.SecondsValue(1.5));
        Assert.Equal("12.35 y", Units.Yalms(12.345f));
        Assert.Equal("0.25 y/s", Units.YalmsPerSecond(0.25f));
    }

    [Theory]
    // Minutes, then seconds to two places, always: 192.9 s is 3 minutes and 12.90 s; 3600 s is 60 minutes.
    [InlineData(0.0, "0:00.00")]
    [InlineData(3.93, "0:03.93")]
    [InlineData(37.32, "0:37.32")]
    [InlineData(192.9, "3:12.90")]
    [InlineData(3600.0, "60:00.00")]
    // 59.999 s rounds to 60.00 s, which is 1:00.00, never 0:60.00.
    [InlineData(59.999, "1:00.00")]
    // Before the start reads as the start.
    [InlineData(-1.0, "0:00.00")]
    public void AClockReadsMinutesAndSeconds(double seconds, string clock)
    {
        Assert.Equal(clock, Units.Clock(seconds));
    }

    [Fact]
    public void HowFarThroughIsTheHeadsClockOverTheWholes()
    {
        Assert.Equal("0:03.93 / 3:12.90", Units.ClockOf(3.93, 192.9));
    }

    [Fact]
    public void ACommaDecimalCultureStillShowsAFullStop()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.50 s", Units.Seconds(1.5));
            Assert.Equal("1.50", Units.SecondsValue(1.5));
            Assert.Equal("0:01.50", Units.Clock(1.5));
            Assert.Equal("2.00 y", Units.Yalms(2f));
            Assert.Equal("3.00 y/s", Units.YalmsPerSecond(3f));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheFieldFormatsShareThoseDecimals()
    {
        Assert.Equal("%.2f s", Units.SecondsField);
        Assert.Equal("%.2f", Units.YalmsField);
        Assert.Equal("%.2f", Units.YalmsPerSecondField);
        Assert.Equal("%.1f°", Units.DegreesField);
    }

    // The tick labels pass the step's own format: 1 yalm at a 0.5 step's "0.0" reads "1.0 y", with a full stop.
    [Fact]
    public void YalmsTakeTheFormatTheyAreGiven() => Assert.Equal("1.0 y", Units.Yalms(1f, "0.0"));
}
