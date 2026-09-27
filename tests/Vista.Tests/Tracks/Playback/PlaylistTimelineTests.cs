using CsCheck;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Playback.PlaybackFixtures;

namespace Vista.Tests.Tracks.Playback;

public class PlaylistTimelineTests
{
    private const double Tolerance = 1e-4;

    // Entries of 5 s once, 2 s three times, 4 s looping forever, then 3 s.
    private static PlaylistTimeline Example(bool loops = false) =>
        new PlaylistPlayback(
            [Item(OneLeg(5f)), Item(OneLeg(2f), 3), Item(OneLeg(4f, loop: true)), Item(OneLeg(3f))],
            loops
        ).Timeline;

    [Fact]
    public void EachEntryIsItsLengthTimesItsRepeatsAndAForeverLoopEndsTheTimeline()
    {
        var timeline = Example();

        // Starts 0, 0 + 5 = 5 and 5 + 2 * 3 = 11; the forever entry is one 4 s pass, so the total is 15 and the 3 s entry after it is left out.
        Assert.Equal([0, 1, 2], timeline.Segments.Select(s => s.Index));
        Assert.Equal([0.0, 5.0, 11.0], timeline.Segments.Select(s => s.Start), (a, b) => Math.Abs(a - b) <= Tolerance);
        Assert.Equal(
            [5.0, 2.0, 4.0],
            timeline.Segments.Select(s => s.PassLength),
            (a, b) => Math.Abs(a - b) <= Tolerance
        );
        Assert.Equal([1, 3, 1], timeline.Segments.Select(s => s.Passes));
        Assert.Equal([false, false, true], timeline.Segments.Select(s => s.LoopsForever));
        Assert.Equal(6.0, timeline.Segments[1].Length, Tolerance);
        Assert.Equal(11.0, timeline.Segments[1].End, Tolerance);
        Assert.Equal(15.0, timeline.Total, Tolerance);
    }

    [Fact]
    public void SegmentsCarryTheirEntryIds()
    {
        PlaylistItem[] items = [Item(OneLeg(5f)), Item(OneLeg(2f))];
        var timeline = new PlaylistPlayback(items).Timeline;

        Assert.Equal(items.Select(i => i.EntryId), timeline.Segments.Select(s => s.EntryId));
    }

    [Fact]
    public void ACountOnALoopingTrackPlaysThatManyPassesAndTheTimelineGoesOn()
    {
        var timeline = new PlaylistPlayback([Item(OneLeg(4f, loop: true), 2), Item(OneLeg(3f))]).Timeline;

        // 4 s twice is 8 s, then the 3 s entry: 11 s.
        Assert.False(timeline.Segments[0].LoopsForever);
        Assert.Equal(2, timeline.Segments.Count);
        Assert.Equal(11.0, timeline.Total, Tolerance);
    }

    [Fact]
    public void APingPongPassIsTheRoundTrip()
    {
        var timeline = new PlaylistPlayback([Item(StraightTrack(direction: PlaybackDirection.PingPong), 2)]).Timeline;

        // The 10 s track there and back is a 20 s pass; twice is 40 s.
        Assert.Equal(20.0, timeline.Segments[0].PassLength, Tolerance);
        Assert.Equal(40.0, timeline.Total, Tolerance);
    }

    [Theory]
    // 8 is 3 s into the 2 s entry starting at 5: its second pass (1), 1 s in.
    [InlineData(8.0, 1, 1, 1.0)]
    // An entry's end is the next one's start; a pass's end is the next pass's start.
    [InlineData(5.0, 1, 0, 0.0)]
    [InlineData(7.0, 1, 1, 0.0)]
    [InlineData(11.0, 2, 0, 0.0)]
    [InlineData(2.5, 0, 0, 2.5)]
    [InlineData(10.5, 1, 2, 1.5)]
    // Before the start is the start; the very end and past it are the forever entry's end.
    [InlineData(-1.0, 0, 0, 0.0)]
    [InlineData(15.0, 2, 0, 4.0)]
    [InlineData(99.0, 2, 0, 4.0)]
    [InlineData(double.NaN, 0, 0, 0.0)]
    public void APlaylistTimeIsAnEntryPassAndTime(double time, int index, int pass, double into)
    {
        var at = Example().At(time);

        Assert.Equal(index, at.Index);
        Assert.Equal(pass, at.Pass);
        Assert.Equal(into, at.Time, Tolerance);
    }

    [Fact]
    public void TheEndOfAPlaylistWithRepeatsIsItsLastPassesEnd()
    {
        var timeline = new PlaylistPlayback([Item(OneLeg(5f)), Item(OneLeg(2f), 3)]).Timeline;

        // Total 5 + 6 = 11: the last entry's third pass (2), all 2 s of it.
        var end = timeline.At(11.0);

        Assert.Equal(1, end.Index);
        Assert.Equal(2, end.Pass);
        Assert.Equal(2.0, end.Time, Tolerance);
    }

    [Fact]
    public void AnEntryPassAndTimeIsAPlaylistTime()
    {
        // Start 5, then two 2 s passes and 1.5 s: 10.5.
        Assert.Equal(10.5, Example().TimeOf(new PlaylistPosition(1, 2, 1.5)), Tolerance);
    }

    [Fact]
    public void AZeroLengthEntryIsOnlyLandedOnAtTheVeryEnd()
    {
        // A lone point with no hold runs for no time.
        var zero = TrackEditing.Append(TrackEditing.Empty(), Point(0f));

        var between = new PlaylistPlayback([Item(OneLeg(5f)), Item(zero), Item(OneLeg(2f))]).Timeline;
        var last = new PlaylistPlayback([Item(OneLeg(5f)), Item(zero)]).Timeline;

        // Its segment is empty at 5, so 5 is the 2 s entry's start; ending the playlist, it's the very end.
        Assert.Equal(new PlaylistPosition(2, 0, 0.0), between.At(between.Segments[1].Start));
        Assert.Equal(new PlaylistPosition(1, 0, 0.0), last.At(last.Total));
    }

    /// <summary>The timeline Live plays for <paramref name="scene"/>'s playlist.</summary>
    private static PlaylistTimeline LiveTimeline(Scene scene)
    {
        var state = new SessionState();
        state.LoadScene(scene);
        return new PlaylistPlayback(state.PlaylistItems(), scene.PlaylistLoops).Timeline;
    }

    [Fact]
    [Trait("Category", "Property")]
    public void APlaylistTimeRoundTripsThroughItsEntryPassAndTime()
    {
        Gen.Select(AnyPlaylistScene, Gen.Double[0.0, 1.0])
            .Sample(
                (scene, share) =>
                {
                    var timeline = LiveTimeline(scene);
                    var time = share * timeline.Total;

                    var at = timeline.At(time);
                    var segment = timeline.Segments[at.Index];

                    Assert.InRange(at.Pass, 0, segment.Passes - 1);
                    Assert.InRange(at.Time, 0.0, segment.PassLength);
                    Assert.Equal(time, timeline.TimeOf(at), 1e-9);
                },
                print: Kept<(Scene Scene, double Share)>(x => $"{SceneJson.Write(x.Scene)}\nShare: {x.Share}")
            );
    }

    [Fact]
    [Trait("Category", "Property")]
    public void AnEntryPassAndTimeRoundTripsThroughItsPlaylistTime()
    {
        Gen.Select(AnyPlaylistScene, Gen.Int[0, 4], Gen.Int[0, 3], Gen.Double[0.001, 0.999])
            .Sample(
                (scene, entry, pass, share) =>
                {
                    var timeline = LiveTimeline(scene);
                    var segment = timeline.Segments[entry % timeline.Segments.Count];
                    var position = new PlaylistPosition(
                        segment.Index,
                        pass % segment.Passes,
                        share * segment.PassLength
                    );

                    var back = timeline.At(timeline.TimeOf(position));

                    Assert.Equal(position.Index, back.Index);
                    Assert.Equal(position.Pass, back.Pass);
                    Assert.Equal(position.Time, back.Time, 1e-9);
                },
                print: Kept<(Scene Scene, int Entry, int Pass, double Share)>(x =>
                    $"{SceneJson.Write(x.Scene)}\nEntry: {x.Entry}, pass: {x.Pass}, share: {x.Share}"
                )
            );
    }
}
