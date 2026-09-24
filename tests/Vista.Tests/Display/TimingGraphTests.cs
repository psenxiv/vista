using System.Numerics;
using Vista.Core.Display;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;

namespace Vista.Tests.Display;

public class TimingGraphTests
{
    private static readonly TimingGraph Graph = new(new Vector2(100f, 50f), new Vector2(400f, 200f), 10f, 20f);

    [Fact]
    public void TimeRunsRightAndDistanceRunsUp()
    {
        Assert.Equal(new Vector2(100f, 250f), Graph.ToScreen(0f, 0f));
        Assert.Equal(new Vector2(500f, 50f), Graph.ToScreen(10f, 20f));
        Assert.Equal(new Vector2(300f, 150f), Graph.ToScreen(5f, 10f));
    }

    [Fact]
    public void ScreenPositionsMapBackAndClamp()
    {
        Assert.Equal(5f, Graph.TimeAt(300f), 4);
        Assert.Equal(10f, Graph.DistanceAt(150f), 4);
        Assert.Equal(0f, Graph.TimeAt(0f));
        Assert.Equal(20f, Graph.DistanceAt(0f));
    }

    [Fact]
    public void AHandleLeavesAlongItsSlope()
    {
        // A second is 40 px across and a distance unit 10 px up, so 4 per second is 45 degrees.
        var key = Graph.ToScreen(5f, 10f);
        var end = Graph.HandleEnd(key, KeySide.Out, 4f, 50f);
        Assert.Equal(end.X - key.X, key.Y - end.Y, 3);
        Assert.Equal(50f, Vector2.Distance(key, end), 3);

        var back = Graph.HandleEnd(key, KeySide.In, 4f, 50f);
        Assert.True(back.X < key.X && back.Y > key.Y);
    }

    [Fact]
    public void DraggingAHandleGivesItsSlopeNeverNegative()
    {
        var key = Graph.ToScreen(5f, 10f);
        Assert.Equal(4f, Graph.SlopeFromHandle(key, KeySide.Out, key + new Vector2(40f, -40f)), 3);
        Assert.Equal(4f, Graph.SlopeFromHandle(key, KeySide.In, key + new Vector2(-40f, 40f)), 3);
        Assert.Equal(0f, Graph.SlopeFromHandle(key, KeySide.Out, key + new Vector2(40f, 30f)));
    }

    // A plot 100 x 50 at the origin, showing 2-6 s and 4-12 yalms: 25 px per second, 6.25 px per yalm.
    private static readonly TimingGraph Zoomed = new(Vector2.Zero, new Vector2(100f, 50f), 10f, 20f)
    {
        TimeFrom = 2f,
        TimeTo = 6f,
        DistanceFrom = 4f,
        DistanceTo = 12f,
    };

    [Fact]
    public void AZoomedGraphMapsTheViewOntoThePlot()
    {
        // 4 s is 2 s into the view, 50 px; 8 y is 4 y above the floor, 25 px up from the bottom at 50.
        var at = Zoomed.ToScreen(4f, 8f);
        Assert.Equal(50f, at.X, 1e-4f);
        Assert.Equal(25f, at.Y, 1e-4f);
    }

    [Fact]
    public void AZoomedGraphReadsTimeAndDistanceWithinTheView()
    {
        Assert.Equal(5f, Zoomed.TimeAt(75f), 1e-4f);
        Assert.Equal(2f, Zoomed.TimeAt(-10f), 1e-4f);
        Assert.Equal(6f, Zoomed.TimeAt(500f), 1e-4f);
        Assert.Equal(4f, Zoomed.DistanceAt(50f), 1e-4f);
        Assert.Equal(12f, Zoomed.DistanceAt(0f), 1e-4f);
    }

    [Fact]
    public void AZoomedGraphReadsHandleSlopesInTheViewsUnits()
    {
        // 25 px across is 1 s and 25 px up is 4 y, so 4 yalms per second; a 10 px handle at that
        // slope runs equally across and up, 10 / sqrt 2 each way.
        Assert.Equal(4f, Zoomed.SlopeFromHandle(Vector2.Zero, KeySide.Out, new Vector2(25f, -25f)), 1e-4f);
        var end = Zoomed.HandleEnd(Vector2.Zero, KeySide.Out, 4f, 10f);
        Assert.Equal(7.0711f, end.X, 1e-3f);
        Assert.Equal(-7.0711f, end.Y, 1e-3f);
    }

    [Fact]
    public void AnOpenEndedReadCarriesOnPastThePlotAtTheViewsScale()
    {
        // 150 px is 1.5 plot widths of a 4 s view from 2 s: 2 + 6 = 8 s. Left of the plot stops at 2 s.
        Assert.Equal(8f, Zoomed.TimeAtOpenEnded(150f), 1e-4f);
        Assert.Equal(2f, Zoomed.TimeAtOpenEnded(-10f), 1e-4f);
    }

    [Fact]
    public void AnOpenEndedReadOfTheWholeShotIsTheOldDragFormula()
    {
        // Graph is 400 px from x = 100 over 10 s: 600 px is 1.25 widths in, 12.5 s.
        Assert.Equal(12.5f, Graph.TimeAtOpenEnded(600f), 1e-4f);
        Assert.Equal(5f, Graph.TimeAtOpenEnded(300f), 1e-4f);
    }
}
