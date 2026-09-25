using System.Numerics;
using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class MarkerHitTestTests
{
    [Fact]
    public void TheNearestMarkerWithinTheRadiusWins()
    {
        var markers = new Vector2?[] { new(100f, 100f), new(106f, 100f), new(300f, 300f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(104f, 100f), 10f));
    }

    [Fact]
    public void NothingWithinTheRadiusIsNull()
    {
        var markers = new Vector2?[] { new(100f, 100f) };
        Assert.Null(MarkerHitTest.Nearest(markers, new Vector2(120f, 100f), 10f));
    }

    [Fact]
    public void TheRadiusIsComparedSquared()
    {
        // 5 px away is 25 px squared: inside 10 squared, but outside an unsquared 10.
        var markers = new Vector2?[] { new(105f, 100f) };
        Assert.Equal(0, MarkerHitTest.Nearest(markers, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void OffScreenMarkersAreSkipped()
    {
        var markers = new Vector2?[] { null, new(100f, 100f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(100f, 100f), 10f));
        Assert.Null(MarkerHitTest.Nearest(Array.Empty<Vector2?>(), Vector2.Zero, 10f));
    }

    [Fact]
    public void ATieGoesToTheLaterMarker()
    {
        // Both markers are 5 px from the cursor.
        var markers = new Vector2?[] { new(95f, 100f), new(105f, 100f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void TheGeneralFormCanGiveATieToTheEarlierItemAndSkipsItemsWithNoPlace()
    {
        // Items 0 and 2 are both 5 px from the cursor; item 1, on it, has no place and is skipped.
        var items = new (Vector2? At, string Name)[] { (new(95f, 100f), "a"), (null, "b"), (new(105f, 100f), "c") };
        var cursor = new Vector2(100f, 100f);

        Assert.Equal(0, MarkerHitTest.Nearest(items, i => i.At, cursor, 10f, laterWinsTie: false));
        Assert.Equal(2, MarkerHitTest.Nearest(items, i => i.At, cursor, 10f, laterWinsTie: true));
    }
}
