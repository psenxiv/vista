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

    // Counts characters, so widths are string lengths.
    private static readonly Func<string, float> Length = s => s.Length;

    // Entries: 1 Opening (5 s), 2 Empty (no points, so not played), 3 Hairpin (2 s three times), 4 Orbit (4 s looping forever).
    // Live plays three segments: Opening 0 to 5, Hairpin 5 to 11, Orbit 11 to 15.
    private static (PlaylistScrub Scrub, Scene Scene) Example()
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
        return (Scrub(scene), scene);
    }

    /// <summary>The bar for <paramref name="scene"/>'s playlist as Live plays it.</summary>
    private static PlaylistScrub Scrub(Scene scene)
    {
        var state = new SessionState();
        state.LoadScene(scene);
        return new PlaylistScrub(new PlaylistPlayback(state.PlaylistItems()).Timeline, state.Scene);
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

        Assert.Equal(0f, Scrub(scene).FractionOf(3.0), Tolerance);
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
        var scrub = Example().Scrub;

        // Hairpin starts at 5 s with 2 s passes: its second and third start at 7 and 9 s, 7/15 and 9/15 of the way.
        Assert.Equal(
            [7f / 15f, 9f / 15f],
            scrub.PassTicks(scrub.Timeline.Segments[1]),
            (a, b) => Math.Abs(a - b) <= Tolerance
        );
        // One pass, and a forever entry's single pass, have none.
        Assert.Empty(scrub.PassTicks(scrub.Timeline.Segments[0]));
        Assert.Empty(scrub.PassTicks(scrub.Timeline.Segments[2]));
    }

    [Fact]
    public void ASegmentIsLabelledWithItsEntrysNumberInThePlaylistAndItsTrack()
    {
        var scrub = Example().Scrub;

        // Hairpin is the second segment but the third entry, after the unplayed Empty.
        Assert.Equal("1 · Opening", scrub.Label(scrub.Timeline.Segments[0]));
        Assert.Equal("3 · Hairpin", scrub.Label(scrub.Timeline.Segments[1]));
        Assert.Equal("4 · Orbit", scrub.Label(scrub.Timeline.Segments[2]));
    }

    [Fact]
    public void AnEntryTheSceneNoLongerHasHasNoLabel()
    {
        var (scrub, scene) = Example();
        var removed = PlaylistEditing.Remove(scene, [scene.Playlist[2].Id]);

        Assert.Null(new PlaylistScrub(scrub.Timeline, removed).Label(scrub.Timeline.Segments[1]));
    }

    [Fact]
    public void TheReadoutIsTheEntryItsTimeInTheEntryAndItsTimeInThePlaylist()
    {
        // 8.1 s is 3.1 s into Hairpin, which runs 5 to 11 s, 6 s long; the playlist is 15 s.
        Assert.Equal("3 · Hairpin  3.10 / 6.00 s   |   8.10 / 15.00 s", Example().Scrub.Readout(1, 8.1, 1000f, Length));
    }

    [Fact]
    public void ANameTooWideForTheBarIsCut()
    {
        // "3 · " and "  3.10 / 6.00 s   |   8.10 / 15.00 s" take 4 + 36 = 40 of 46 characters,
        // leaving 6 for the 7-letter Hairpin: its start "Hai" and the three-stop ellipsis.
        Assert.Equal("3 · Hai...  3.10 / 6.00 s   |   8.10 / 15.00 s", Example().Scrub.Readout(1, 8.1, 46f, Length));
    }

    [Fact]
    public void AnEntryTheSceneNoLongerHasReadsOutItsTimesAlone()
    {
        var (scrub, scene) = Example();
        var removed = PlaylistEditing.Remove(scene, [scene.Playlist[2].Id]);

        Assert.Equal(
            "3.10 / 6.00 s   |   8.10 / 15.00 s",
            new PlaylistScrub(scrub.Timeline, removed).Readout(1, 8.1, 1000f, Length)
        );
    }
}
