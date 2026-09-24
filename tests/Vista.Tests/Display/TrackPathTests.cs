using System.Numerics;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class TrackPathTests
{
    private static readonly Vector3[] Line = [Vector3.Zero, new(10f, 0f, 0f), new(20f, 0f, 0f)];

    [Fact]
    public void FewerThanTwoPointsComeBackAsTheyAre()
    {
        Assert.Empty(TrackPath.Sample([], 0.5f));
        Assert.Equal(new[] { new Vector3(1f, 2f, 3f) }, TrackPath.Sample([new Vector3(1f, 2f, 3f)], 0.5f));
    }

    [Fact]
    public void TheSamplesStartAndEndOnTheTrackAndPassThroughEachPoint()
    {
        var samples = TrackPath.Sample(Line, 0.5f);
        Assert.Equal(Line[0], samples[0]);
        Assert.Equal(Line[2].X, samples[^1].X, 3);
        Assert.Contains(samples, s => Vector3.Distance(s, Line[1]) < 1e-3f);
    }

    [Fact]
    public void NeighbouringSamplesAreNoFurtherApartThanTheSpacing()
    {
        var samples = TrackPath.Sample(Line, 0.5f);
        for (var i = 1; i < samples.Count; i++)
            Assert.True(Vector3.Distance(samples[i - 1], samples[i]) <= 0.5f * 1.05f);
    }

    [Fact]
    public void CoincidentPointsStillGiveOneSamplePerSegment()
    {
        var samples = TrackPath.Sample([Vector3.One, Vector3.One], 0.5f);
        Assert.Equal(2, samples.Count);
    }
}
