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
}
