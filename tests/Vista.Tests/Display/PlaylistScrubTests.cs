using Vista.Core.Display;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Playback.PlaybackFixtures;

namespace Vista.Tests.Display;

public class PlaylistScrubTests
{
    private const float Tolerance = 1e-5f;

    // Entries: 1 Opening (5 s), 2 Empty (no points, so not played), 3 Hairpin (2 s three times), 4 Orbit (4 s looping forever).
    // Live plays three segments: Opening 0 to 5, Hairpin 5 to 11, Orbit 11 to 15.
    private static (PlaylistScrub Scrub, PlaylistTimeline Timeline, Scene Scene) Example()
    {
        Track[] tracks =
        [
            OneLeg(5f) with
            {
                Name = "Opening",
            },
            TrackEditing.Empty() with
            {
                Name = "Empty",
            },
            OneLeg(2f) with
            {
                Name = "Hairpin",
            },
            OneLeg(4f, loop: true) with
            {
                Name = "Orbit",
            },
        ];
        var scene = PlaylistEditing.Add(new Scene(tracks, new HashSet<Guid>(), []), tracks.Select(t => t.Id).ToArray());
        scene = PlaylistEditing.SetLoops(scene, scene.Playlist[2].Id, 3);
        var (scrub, timeline) = Scrub(scene);
        return (scrub, timeline, scene);
    }

    /// <summary>The bar for <paramref name="scene"/>'s playlist as Live plays it, and the timeline it lays out.</summary>
    private static (PlaylistScrub Scrub, PlaylistTimeline Timeline) Scrub(Scene scene)
    {
        var state = new SessionState();
        state.LoadScene(scene);
        var timeline = new PlaylistPlayback(state.PlaylistItems()).Timeline;
        return (new PlaylistScrub(timeline, state.Scene), timeline);
    }

    /// <summary>The example's bar zoomed to <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static (PlaylistScrub Scrub, PlaylistTimeline Timeline) Zoomed(float from, float to)
    {
        var (_, timeline, scene) = Example();
        return (new PlaylistScrub(timeline, scene, new TimingView(from, to)), timeline);
    }

    [Theory]
    // Zoomed to Hairpin's 5 to 11 s, 8 s is halfway along; times outside the view hold at the ends.
    [InlineData(8.0, 0.5f)]
    [InlineData(2.0, 0f)]
    [InlineData(12.0, 1f)]
    public void ZoomedATimeIsItsShareOfTheView(double time, float fraction)
    {
        Assert.Equal(fraction, Zoomed(5f, 11f).Scrub.FractionOf(time), Tolerance);
    }

    [Fact]
    public void ZoomedAPlaceAlongTheBarIsInTheView()
    {
        // Halfway along 5 to 11 s is 8 s, in Hairpin, the second segment.
        var bar = Zoomed(5f, 11f).Scrub;

        Assert.Equal(8.0, bar.TimeAt(0.5f), Tolerance);
        Assert.Equal(1, bar.SegmentAt(0.5f).Index);
    }

    [Fact]
    public void ZoomedOnlyTheTicksInTheViewShow()
    {
        // Hairpin's repeats start at 7 and 9 s: over 5 to 11 s they're 2/6 and 4/6 along; over 8 to 11 s only 9 s shows, 1/3 along.
        var hairpin = Zoomed(5f, 11f).Timeline.Segments[1];

        Assert.Equal(
            [1f / 3f, 2f / 3f],
            Zoomed(5f, 11f).Scrub.PassTicks(hairpin),
            (x, y) => Math.Abs(x - y) <= Tolerance
        );
        Assert.Equal([1f / 3f], Zoomed(8f, 11f).Scrub.PassTicks(hairpin), (x, y) => Math.Abs(x - y) <= Tolerance);
    }

    [Fact]
    public void ZoomedOnlyTheSegmentsOverlappingTheViewAreVisible()
    {
        // Segments run 0 to 5, 5 to 11 and 11 to 15 s. Over 6 to 10 s only Hairpin is visible; over 4 to 12 s all three are.
        // Over exactly 5 to 11 s, Opening only touches the view's start and Orbit its end, so neither shows.
        var inside = Zoomed(6f, 10f);
        var across = Zoomed(4f, 12f);
        var exact = Zoomed(5f, 11f);
        var segments = inside.Timeline.Segments;

        Assert.Equal([false, true, false], segments.Select(inside.Scrub.Visible));
        Assert.Equal([true, true, true], segments.Select(across.Scrub.Visible));
        Assert.Equal([false, true, false], segments.Select(exact.Scrub.Visible));
    }

    [Theory]
    // Over 5 to 11 s, the head shows from 5 to 11 s and not outside.
    [InlineData(4.0, false)]
    [InlineData(5.0, true)]
    [InlineData(8.0, true)]
    [InlineData(11.0, true)]
    [InlineData(12.0, false)]
    public void ZoomedTheHeadShowsOnlyInTheView(double head, bool shows)
    {
        Assert.Equal(shows, Zoomed(5f, 11f).Scrub.Shows(head));
    }

    [Theory]
    // 7.5 of 15 s is halfway; before the start and past the end hold at the ends.
    [InlineData(7.5, 0.5f)]
    [InlineData(0.0, 0f)]
    [InlineData(15.0, 1f)]
    [InlineData(-1.0, 0f)]
    [InlineData(20.0, 1f)]
    public void ATimeIsItsShareOfThePlaylist(double time, float fraction)
    {
        Assert.Equal(fraction, Example().Scrub.FractionOf(time), Tolerance);
    }

    [Fact]
    public void APlaylistWithNoLengthPutsEveryTimeAtTheStart()
    {
        // A lone point with no hold runs for no time.
        var zero = TrackEditing.Append(TrackEditing.Empty(), Point(0f));
        var scene = PlaylistEditing.Add(new Scene([zero], new HashSet<Guid>(), []), [zero.Id]);

        Assert.Equal(0f, Scrub(scene).Scrub.FractionOf(3.0), Tolerance);
    }

    [Theory]
    // A fifth of 15 s is 3 s; the ends hold outside the bar.
    [InlineData(0.2f, 3.0)]
    [InlineData(1f, 15.0)]
    [InlineData(-0.5f, 0.0)]
    [InlineData(1.5f, 15.0)]
    public void APlaceAlongTheBarIsItsShareOfThePlaylist(float fraction, double time)
    {
        Assert.Equal(time, Example().Scrub.TimeAt(fraction), 1e-5);
    }

    [Theory]
    // 0.2 is 3 s, in Opening; 0.5 is 7.5 s, in Hairpin; 0.9 is 13.5 s, in Orbit.
    [InlineData(0.2f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(0.9f, 2)]
    // 0.34 is 5.1 s, just into Hairpin, which starts at 5 s (a third of the way).
    [InlineData(0.34f, 1)]
    public void APlaceAlongTheBarIsInTheSegmentPlayingThen(float fraction, int segment)
    {
        Assert.Equal(segment, Example().Scrub.SegmentAt(fraction).Index);
    }

    [Fact]
    public void RepeatsHaveATickWhereEachPassAfterTheFirstStarts()
    {
        var (scrub, timeline, _) = Example();

        // Hairpin starts at 5 s with 2 s passes: its second and third start at 7 and 9 s, 7/15 and 9/15 of the way.
        Assert.Equal(
            [7f / 15f, 9f / 15f],
            scrub.PassTicks(timeline.Segments[1]),
            (a, b) => Math.Abs(a - b) <= Tolerance
        );
        // One pass, and a forever entry's single pass, have none.
        Assert.Empty(scrub.PassTicks(timeline.Segments[0]));
        Assert.Empty(scrub.PassTicks(timeline.Segments[2]));
    }

    [Fact]
    public void ASegmentIsLabelledWithItsEntrysNumberInThePlaylistAndItsTrack()
    {
        var (scrub, timeline, _) = Example();

        // Hairpin is the second segment but the third entry, after the unplayed Empty.
        Assert.Equal("1 · Opening", scrub.Label(timeline.Segments[0]));
        Assert.Equal("3 · Hairpin", scrub.Label(timeline.Segments[1]));
        Assert.Equal("4 · Orbit", scrub.Label(timeline.Segments[2]));
    }

    [Fact]
    public void AnEntryTheSceneNoLongerHasHasNoLabel()
    {
        var (_, timeline, scene) = Example();
        var removed = PlaylistEditing.Remove(scene, [scene.Playlist[2].Id]);

        Assert.Null(new PlaylistScrub(timeline, removed).Label(timeline.Segments[1]));
    }
}
