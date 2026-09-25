using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimedChannelTests
{
    private static TimedChannel Channel(float[] values, float[] arrive, float[]? depart = null) =>
        new(values, arrive, depart ?? arrive);

    [Fact]
    public void EveryPointIsHitAtItsTime()
    {
        float[] values = [1f, 4f, -2f, 6f];
        var channel = Channel(values, [0f, 1f, 3f, 4f]);

        for (var i = 0; i < values.Length; i++)
            Assert.Equal(values[i], channel.At(new[] { 0f, 1f, 3f, 4f }[i]), 1e-5f);
    }

    [Fact]
    public void TheRateCarriesStraightThroughAPointBetweenLegsOfDifferentTimes()
    {
        // Legs of 2 s and 1 s rising 10 and 20: the slope at the middle is (10/2·1 + 20/1·2) / 3 = 15 per second, from both sides.
        var channel = Channel([0f, 10f, 30f], [0f, 2f, 3f]);

        var (left, right) = Slopes(channel.At, 2.0, 1e-3);

        Assert.Equal(15f, left, 0.05f);
        Assert.Equal(15f, right, 0.05f);
    }

    [Fact]
    public void AnEndPointTakesHalfItsLegsSlope()
    {
        // One 2 s leg rising 10: both end slopes are half of 5, 2.5 per second, so the tangents are 5 over the leg.
        // A quarter of the way, Hermite gives 5·0.140625 + 10·0.15625 + 5·(−0.046875) = 2.03125.
        Assert.Equal(2.03125f, Channel([0f, 10f], [0f, 2f]).At(0.5), 1e-4f);
    }

    [Fact]
    public void AHeldPointHoldsStillAndEasesInAndOut()
    {
        // Point 1 holds from 2 s to 3 s.
        var channel = Channel([0f, 10f, 20f], [0f, 2f, 5f], [0f, 3f, 5f]);

        Assert.Equal(10f, channel.At(2.5), 1e-5f);
        Assert.Equal(0f, Slopes(channel.At, 2.0, 1e-3).Left, 0.05f);
        Assert.Equal(0f, Slopes(channel.At, 3.0, 1e-3).Right, 0.05f);
    }

    [Fact]
    public void AHeldPointHasItsOwnValueAtTheInstantItIsReached()
    {
        // Point 1, whose value is 0, is reached at 10.43251 s and holds; these times put the leg's fraction a hair off 1 there.
        var channel = Channel([-0.45614034f, 0f, 0f], [0f, 10.43251f, 12f], [0.9230769f, 10.514927f, 12f]);

        Assert.Equal(0f, channel.At(10.43251f));
    }

    [Fact]
    public void ALegOfNoTimeGivesNoSlope()
    {
        // The second leg takes no time, so point 1's slope is 0; point 0's is half of 10/1. Halfway: 5·0.125 + 10·0.5 = 5.625.
        var channel = Channel([0f, 10f, 20f], [0f, 1f, 1f]);

        Assert.Equal(5.625f, channel.At(0.5), 1e-4f);
        Assert.Equal(20f, channel.At(1.0), 1e-5f);
    }

    [Fact]
    public void BeforeTheFirstAndAfterTheLastItHolds()
    {
        var channel = Channel([3f, 7f], [1f, 2f]);

        Assert.Equal(3f, channel.At(0.0), 1e-5f);
        Assert.Equal(7f, channel.At(9.0), 1e-5f);
        Assert.Equal(4f, Channel([4f], [0f]).At(5.0), 1e-5f);
    }

    [Fact]
    public void MismatchedListsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new TimedChannel([1f, 2f], [0f], [0f, 1f]));
        Assert.Throws<ArgumentException>(() => new TimedChannel([], [], []));
    }
}
