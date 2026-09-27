using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class PathChannelTests
{
    [Fact]
    public void EveryPointIsHitAtItsDistance()
    {
        float[] values = [1f, 4f, -2f, 6f];
        float[] distances = [0f, 1f, 3f, 4f];
        var channel = new PathChannel(values, distances);

        for (var i = 0; i < values.Length; i++)
            Assert.Equal(values[i], channel.At(distances[i]), 1e-5f);
    }

    [Fact]
    public void TheRateCarriesStraightThroughAPointBetweenLegsOfDifferentLengths()
    {
        // Legs of 2 and 1 yalms rising 10 and 20: the slope at the middle is (10/2·1 + 20/1·2) / 3 = 15 per yalm, from both
        // sides: exactly the bound, 3 times the gentler leg's 5.
        var channel = new PathChannel([0f, 10f, 30f], [0f, 2f, 3f]);

        var (left, right) = Slopes(d => channel.At((float)d), 2.0, 1e-3);

        Assert.Equal(15f, left, 0.05f);
        Assert.Equal(15f, right, 0.05f);
    }

    [Fact]
    public void AChannelTakesTheThreePointSlopeThroughAPointAndTheEndRuleAtItsEnds()
    {
        // Values 0, 10, 40 at distances 0, 2, 6: changes 10 over 2 and 30 over 4.
        // Through point 1: ThroughWeights(2, 4) = (4/12, 2/24) → 10·4/12 + 30·2/24 = 3.3333 + 2.5 = 5.8333 per yalm.
        // Midway along leg 1 (distance 4, u = 0.5): Hermite(10, 40, 5.8333·4, m₂·4, 0.5), with the end slope at point 2
        // EndWeights(4, 2) = ((8 + 2)/(4·6), −4/(2·6)) = (0.41667, −0.33333) → 30·0.41667 − 10·0.33333 = 9.1667 per yalm:
        // 0.5·10 + 0.125·23.333 + 0.5·40 − 0.125·36.667 = 5 + 2.9167 + 20 − 4.5833 = 23.333. Neither is limited: the legs
        // rise at 5 and 7.5 per yalm, so 5.8333 is 1.17 times the gentler 5, and 9.1667 is 1.22 times the end leg's 7.5.
        var channel = new PathChannel([0f, 10f, 40f], [0f, 2f, 6f]);

        Assert.Equal(23.333f, channel.At(4f), 1e-3f);
        Assert.Equal(0f, channel.At(-1f));
        Assert.Equal(40f, channel.At(7f));
    }

    [Fact]
    public void APointWhereTheChangeReversesHasNoSlope()
    {
        // Values 0, 40, 0 a yalm apart: the legs change opposite ways, so point 1's slope is 0. Point 0's end rule is
        // EndWeights(1, 1)·(40, −40) = 1.5·40 + 0.5·40 = 80 per yalm, twice its leg's 40, inside the bound; point 2's is
        // −80 by symmetry. Halfway along each leg: 0.5·40 + 0.125·80 = 30.
        var channel = new PathChannel([0f, 40f, 0f], [0f, 1f, 2f]);

        Assert.Equal(30f, channel.At(0.5f), 1e-4f);
        Assert.Equal(30f, channel.At(1.5f), 1e-4f);
    }

    [Fact]
    public void ASlopeIsAtMostThreeTimesTheGentlerLegsRate()
    {
        // Values 0, 40, 44 a yalm apart: legs of 40 and 4 per yalm. Point 1's three-point slope, ThroughWeights(1, 1) =
        // (0.5, 0.5), is 0.5·40 + 0.5·4 = 22, limited to 3·4 = 12. Point 0's end rule, EndWeights(1, 1)·(40, 4) = 1.5·40 −
        // 0.5·4 = 58, is 1.45 times its leg's 40 and stays; point 2's, 1.5·4 − 0.5·40 = −14, runs against its leg and is 0.
        // Halfway along leg 1: 0.5·40 + 0.125·58 − 0.125·12 = 25.75. Halfway along leg 2: 0.5·40 + 0.125·12 + 0.5·44 = 43.5.
        var channel = new PathChannel([0f, 40f, 44f], [0f, 1f, 2f]);

        Assert.Equal(25.75f, channel.At(0.5f), 1e-4f);
        Assert.Equal(43.5f, channel.At(1.5f), 1e-4f);
    }

    [Fact]
    public void TwoPointsBlendAtOneRate()
    {
        // The secant at both ends, 10 per 2 yalms: a straight blend, 7.5 at distance 1.5.
        Assert.Equal(7.5f, new PathChannel([0f, 10f], [0f, 2f]).At(1.5f), 1e-4f);
    }

    [Fact]
    public void BeforeTheFirstAndAfterTheLastItHolds()
    {
        var channel = new PathChannel([3f, 7f], [1f, 2f]);

        Assert.Equal(3f, channel.At(0f), 1e-5f);
        Assert.Equal(7f, channel.At(9f), 1e-5f);
        Assert.Equal(4f, new PathChannel([4f], [0f]).At(5f), 1e-5f);
    }

    [Fact]
    public void MismatchedOrUnorderedListsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new PathChannel([1f, 2f], [0f]));
        Assert.Throws<ArgumentException>(() => new PathChannel([], []));
        Assert.Throws<ArgumentException>(() => new PathChannel([1f, 2f], [1f, 1f]));
    }
}
