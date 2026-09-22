using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

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
    public void OffScreenMarkersAreSkipped()
    {
        var markers = new Vector2?[] { null, new(100f, 100f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(100f, 100f), 10f));
        Assert.Null(MarkerHitTest.Nearest(Array.Empty<Vector2?>(), Vector2.Zero, 10f));
    }
}
