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
        Assert.Equal("12.35 y", Units.Yalms(12.345f));
        Assert.Equal("0.25 y/s", Units.YalmsPerSecond(0.25f));
    }

    [Fact]
    public void ACommaDecimalCultureStillShowsAFullStop()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.50 s", Units.Seconds(1.5));
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
}
