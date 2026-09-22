using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class PlaylistPlaybackTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Points at x = 0, 5, 10 in two 5 s legs: a 10 s track.
    private static Track Ten(bool loop = false, PlaybackDirection direction = PlaybackDirection.Forward)
    {
        var track = TrackEditing.SetDirection(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), direction);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    // A single point at x held for the given seconds.
    private static Track Snap(float x, float hold, bool loop = false)
        => TrackEditing.SetHold(TrackEditing.SetLoop(TrackEditing.Append(TrackEditing.Empty(), Point(x)), loop), 0, hold);

    private static PlaylistItem Item(Track track, int? loops = null) => new(Guid.NewGuid(), track, loops);

    [Fact]
    public void AnEmptyPlaylistIsRefused() => Assert.Throws<ArgumentException>(() => new PlaylistPlayback([]));

    [Fact]
    public void EntriesPlayInTurnCarryingTimeOver()
    {
        var items = new[] { Item(Ten()), Item(Ten()) };
        var playback = new PlaylistPlayback(items);

        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(items[1].EntryId, playback.EntryId);
        Assert.Equal(2.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALongFramePassesThroughShortEntries()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Snap(50f, 1f)), Item(Ten())]);
        playback.Advance(12f);

        Assert.Equal(2, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void TheLastFrameHoldsAtTheEnd()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        var atEnd = playback.Advance(25f);

        Assert.True(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.Equal(atEnd, playback.Advance(5f));
    }

    [Fact]
    public void ALoopCountPlaysTheTrackThatManyTimes()
    {
        var playback = new PlaylistPlayback([Item(Ten(), 3), Item(Ten())]);
        playback.Advance(25f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);

        playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);
    }

    [Fact]
    public void APingPongLoopIsOneRoundTrip()
    {
        var playback = new PlaylistPlayback([Item(Ten(direction: PlaybackDirection.PingPong), 1), Item(Ten())]);
        playback.Advance(15f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);

        playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopingTrackWithNoCountHoldsThePlaylist()
    {
        var playback = new PlaylistPlayback([Item(Ten(loop: true)), Item(Ten())]);
        playback.Advance(1003f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ACountOverridesTheTracksLoop()
    {
        var playback = new PlaylistPlayback([Item(Ten(loop: true), 1), Item(Ten())]);
        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopCountBelowOneActsAsOne()
    {
        var playback = new PlaylistPlayback([Item(Ten(loop: true), 0), Item(Ten())]);
        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void AZeroLengthEntryIsShownForOneFrame()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Snap(50f, 0f)), Item(Ten())]);

        var reached = playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(50f, reached!.Value.Position.X, 4);

        playback.Advance(0.5f);
        Assert.Equal(2, playback.Index);
        Assert.Equal(0.5, playback.ShotTime, 4);
    }

    [Fact]
    public void AZeroLengthFirstEntryIsShownBeforeMovingOn()
    {
        var playback = new PlaylistPlayback([Item(Snap(50f, 0f)), Item(Ten())]);

        var shown = playback.Advance(0.016f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(50f, shown!.Value.Position.X, 4);

        playback.Advance(0.5f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(0.5, playback.ShotTime, 4);

        playback.Restart();
        var shownAgain = playback.Advance(0.016f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(50f, shownAgain!.Value.Position.X, 4);
    }

    [Fact]
    public void ASnapPointHoldsForItsHoldThenMovesOn()
    {
        var playback = new PlaylistPlayback([Item(Snap(50f, 3f)), Item(Ten())]);

        var held = playback.Advance(2f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(50f, held!.Value.Position.X, 4);

        playback.Advance(2f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopingSnapPointHoldsThePlaylist()
    {
        var playback = new PlaylistPlayback([Item(Snap(50f, 0f, loop: true)), Item(Ten())]);
        playback.Advance(100f);
        Assert.Equal(0, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingStaysInTheCurrentLoopPass()
    {
        var playback = new PlaylistPlayback([Item(Ten(), 2), Item(Ten())]);
        playback.Advance(13f);

        playback.Seek(8.0);
        Assert.Equal(8.0, playback.ShotTime, 4);

        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(9.0, playback.ShotTime, 4);

        playback.Advance(1f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingAFinishedPlaylistBackUnfinishesIt()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        playback.Advance(25f);

        playback.Seek(3.0);

        Assert.False(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 4);
    }

    [Fact]
    public void RestartGoesBackToTheFirstEntry()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        playback.Advance(25f);

        playback.Restart();

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime);
        Assert.False(playback.IsFinished);
        Assert.Equal(10.0, playback.ShotLength, 4);
    }
}
