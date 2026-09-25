using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class EvaluatorCacheTests
{
    [Fact]
    public void TheSameTrackKeepsItsEvaluatorAndAnotherGetsANewOne()
    {
        var cache = new EvaluatorCache();
        var track = Build3PointTrack();
        var first = cache.For(track);

        Assert.Same(first, cache.For(track));

        // An equal track that is another instance counts as another track.
        var copy = track with
        { };
        Assert.NotSame(first, cache.For(copy));
        Assert.Same(cache.For(copy), cache.For(copy));
    }
}
