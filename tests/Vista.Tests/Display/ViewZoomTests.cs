using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class ViewZoomTests
{
    [Fact]
    public void ANewZoomShowsTheWholeLength()
    {
        var zoom = new ViewZoom<int>();

        Assert.Equal(new TimingView(0f, 10f), zoom.View(1, 10f));
        Assert.False(zoom.Zoomed);
    }

    [Fact]
    public void OneNotchInZoomsByTheNotchFactorAroundTheAnchor()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);

        zoom.Zoom(anchor: 5f, notches: 1f, length: 10f);

        // Span 10 / 1.25 = 8 s, anchor 5 s kept halfway across: 1 s to 9 s.
        var view = zoom.View(1, 10f);
        Assert.Equal(1f, view.From, 1e-5f);
        Assert.Equal(9f, view.To, 1e-5f);
        Assert.True(zoom.Zoomed);
    }

    [Fact]
    public void ANewKeyShowsTheWholeLengthAgainAndSaysSo()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);
        zoom.Zoom(5f, 1f, 10f);
        Assert.False(zoom.View(1, 10f) == new TimingView(0f, 10f));
        Assert.False(zoom.Restarted);

        Assert.Equal(new TimingView(0f, 20f), zoom.View(2, 20f));
        Assert.True(zoom.Restarted);
        Assert.False(zoom.Zoomed);
    }

    [Fact]
    public void AShorterLengthClampsTheView()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);
        zoom.Set(new TimingView(6f, 9f), 10f);

        // 3 s wide, slid back to end at the new 8 s length: 5 s to 8 s.
        Assert.Equal(new TimingView(5f, 8f), zoom.View(1, 8f));
    }

    [Fact]
    public void ZoomingOutToTheWholeLengthIsNoLongerZoomed()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);
        zoom.Zoom(5f, 1f, 10f);

        zoom.Zoom(5f, -1f, 10f);

        Assert.False(zoom.Zoomed);
        Assert.Equal(new TimingView(0f, 10f), zoom.View(1, 10f));
    }

    [Fact]
    public void NoNotchesChangeNothing()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);

        zoom.Zoom(5f, 0f, 10f);

        Assert.False(zoom.Zoomed);
    }

    [Fact]
    public void ResetShowsTheWholeLength()
    {
        var zoom = new ViewZoom<int>();
        zoom.View(1, 10f);
        zoom.Set(new TimingView(2f, 4f), 10f);

        zoom.Reset();

        Assert.False(zoom.Zoomed);
    }
}
