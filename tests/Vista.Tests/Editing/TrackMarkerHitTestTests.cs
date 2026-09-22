using System.Numerics;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class TrackMarkerHitTestTests
{
    private static readonly Guid Edited = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();

    [Fact]
    public void TheEditedTracksMarkerWinsEvenWhenAnotherIsNearer()
    {
        var markers = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Edited, 3, new Vector2(108f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(101f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksMarkerIsHitWhenNoEditedMarkerIsInRange()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, 0, new Vector2(300f, 300f)),
            new TrackMarker(Other, 2, new Vector2(100f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(102f, 100f), 10f));
    }

    [Fact]
    public void AmongOtherTracksTheNearestWinsAndATieGoesToTheLaterMarker()
    {
        var nearer = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Third, 0, new Vector2(106f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(nearer, Edited, new Vector2(105f, 100f), 10f));

        var tie = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Third, 0, new Vector2(100f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(tie, Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void OffScreenAndOutOfRangeMarkersAreNotHit()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, 0, null),
            new TrackMarker(Other, 0, new Vector2(200f, 200f)),
        };
        Assert.Null(TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Null(TrackMarkerHitTest.Nearest([], Edited, Vector2.Zero, 10f));
    }
}
