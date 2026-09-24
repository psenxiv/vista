using System.Numerics;
using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

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

    [Fact]
    public void APointBeatsItsOwnTracksAnchor()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, -1, new Vector2(100f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Edited, 0, new Vector2(106f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(101f, 100f), 10f));
    }

    [Fact]
    public void TheEditedTracksAnchorBeatsAnotherTracksPoint()
    {
        var markers = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Edited, -1, new Vector2(106f, 100f), MarkerKind.TrackAnchor),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksPointBeatsAnotherTracksAnchorAndTheSceneAnchorComesLast()
    {
        var markers = new[]
        {
            new TrackMarker(Guid.Empty, -1, new Vector2(100f, 100f), MarkerKind.SceneAnchor),
            new TrackMarker(Other, -1, new Vector2(103f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Other, 2, new Vector2(107f, 100f)),
        };
        Assert.Equal(2, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers[..2], Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(0, TrackMarkerHitTest.Nearest(markers[..1], Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void TheEditedTracksLookAtComesAfterItsAnchorAndBeforeOtherTracks()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, -1, new Vector2(100f, 100f), MarkerKind.LookAt),
            new TrackMarker(Edited, -1, new Vector2(104f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
        };

        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(0, TrackMarkerHitTest.Nearest([markers[0], markers[2]], Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksLookAtComesAfterItsAnchorAndBeforeTheSceneAnchor()
    {
        var markers = new[]
        {
            new TrackMarker(Guid.Empty, -1, new Vector2(100f, 100f), MarkerKind.SceneAnchor),
            new TrackMarker(Other, -1, new Vector2(105f, 100f), MarkerKind.LookAt),
            new TrackMarker(Other, -1, new Vector2(108f, 100f), MarkerKind.TrackAnchor),
        };

        Assert.Equal(2, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers[..2], Edited, new Vector2(100f, 100f), 10f));
    }
}
