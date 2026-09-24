using Vista.Core.Editing;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Editing;

public class TimingViewTests
{
    private static void Is(float from, float to, TimingView view)
    {
        Assert.Equal(from, view.From, 1e-4f);
        Assert.Equal(to, view.To, 1e-4f);
    }

    [Fact]
    public void ZoomingKeepsTheAnchorWhereItWas()
    {
        // Halving 0-10 leaves 5 s. Around 5 s, the anchor's middle place gives 2.5-7.5; around
        // 2 s, a fifth of the way in, it gives 2 - 1 = 1 to 6.
        Is(2.5f, 7.5f, TimingView.Whole(10f).Zoom(5f, 0.5f, 10f));
        Is(1f, 6f, TimingView.Whole(10f).Zoom(2f, 0.5f, 10f));
    }

    [Fact]
    public void ZoomingOutStopsAtTheWholeShot()
    {
        var view = new TimingView(2f, 6f).Zoom(4f, 10f, 10f);

        Is(0f, 10f, view);
        Assert.True(view.IsWhole(10f));
    }

    [Fact]
    public void ZoomingInStopsAtTheMinimumSpan()
    {
        // A hundredth of 1 s is below 0.2 s, so it stops there, centred on 0.5 s: 0.4 to 0.6.
        Is(0.4f, 0.6f, new TimingView(0f, 1f).Zoom(0.5f, 0.01f, 10f));
    }

    [Fact]
    public void PanningStopsAtEitherEndOfTheShot()
    {
        Is(4f, 8f, new TimingView(2f, 6f).Pan(3f, 8f));
        Is(0f, 4f, new TimingView(2f, 6f).Pan(-5f, 8f));
    }

    [Fact]
    public void AViewFollowsAShotThatShrinks()
    {
        Is(2f, 6f, new TimingView(4f, 8f).Clamp(6f));
        Is(0f, 3f, new TimingView(4f, 8f).Clamp(3f));
    }

    [Fact]
    public void TheDistanceAxisFitsTheView()
    {
        // Build3PointTrack's keys sit at 0, 5 and 10 s and 0, 10 and 20 yalms. Both timing secants
        // are 2, so the distance curve is the line d = 2t: 2.5-7.5 s covers 5-15 yalms.
        var (from, to) = new TimingView(2.5f, 7.5f).Distances(new TrackEvaluator(Build3PointTrack()));

        Assert.Equal(5f, from, 1e-3f);
        Assert.Equal(15f, to, 1e-3f);
    }

    [Fact]
    public void AFlatStretchWidensToAYalm()
    {
        // A 2 s hold at point 1 keeps the camera at 10 yalms from 5 s to 7 s.
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var (from, to) = new TimingView(5.5f, 6.5f).Distances(new TrackEvaluator(track));

        Assert.Equal(9.5f, from, 1e-3f);
        Assert.Equal(10.5f, to, 1e-3f);
    }

    [Fact]
    public void TickStepsAreRound()
    {
        // 60 over 6 is 10 exactly; 15.3 over 16 is 0.96, rounding up to 1; 2 over 16 is 0.125, up to 0.2.
        Assert.Equal(10f, Ticks.Step(60f, 6), 1e-5f);
        Assert.Equal(1f, Ticks.Step(15.3f, 16), 1e-5f);
        Assert.Equal(0.2f, Ticks.Step(2f, 16), 1e-5f);
    }
}
