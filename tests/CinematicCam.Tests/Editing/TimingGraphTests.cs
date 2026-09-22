using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

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
}
