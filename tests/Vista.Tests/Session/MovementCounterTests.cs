using Vista.Core.Session;
using Xunit;

namespace Vista.Tests.Session;

public class MovementCounterTests
{
    // The counter is plausible from 0 (nobody holds it) to 16 holds, the spec's provisional bound, inclusive.
    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    [InlineData(int.MaxValue, false)]
    public void ACountIsPlausibleFromZeroToSixteen(int count, bool plausible) =>
        Assert.Equal(plausible, MovementCounter.IsPlausible(count));

    // An image from 0x1000 to 0x1100: the counter's four bytes fit from 0x1000 up to 0x10FC (0x10FC + 4 = 0x1100).
    [Theory]
    [InlineData(0x0FFF, false)]
    [InlineData(0x1000, true)]
    [InlineData(0x10FC, true)]
    [InlineData(0x10FD, false)]
    [InlineData(0x1100, false)]
    public void ACounterIsReadOnlyInsideTheImage(long address, bool inside) =>
        Assert.Equal(inside, MovementCounter.InImage(address, 0x1000, 0x100));
}
