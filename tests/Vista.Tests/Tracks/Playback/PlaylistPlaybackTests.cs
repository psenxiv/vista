using System.Numerics;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Playback.PlaybackFixtures;

namespace Vista.Tests.Tracks.Playback;

public class PlaylistPlaybackTests
{
    // A single point at x held for the given seconds.
    private static Track Snap(float x, float hold, bool loop = false) =>
        TrackEditing.SetHold(TrackEditing.SetLoop(TrackEditing.Append(TrackEditing.Empty(), Point(x)), loop), 0, hold);

    private static PlaylistItem Item(Track track, int? loops = null) => new(Guid.NewGuid(), track, loops);

    [Fact]
    public void AnEmptyPlaylistIsRefused() => Assert.Throws<ArgumentException>(() => new PlaylistPlayback([]));

    [Fact]
    public void EntriesPlayInTurnCarryingTimeOver()
    {
        var items = new[] { Item(StraightTrack()), Item(StraightTrack()) };
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
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(Snap(50f, 1f)), Item(StraightTrack())]);
        playback.Advance(12f);

        Assert.Equal(2, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void TheLastFrameHoldsAtTheEnd()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);
        var atEnd = playback.Advance(25f);

        Assert.True(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.Equal(atEnd, playback.Advance(5f));
    }

    [Fact]
    public void ALoopCountPlaysTheTrackThatManyTimes()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(), 3), Item(StraightTrack())]);
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
        var playback = new PlaylistPlayback([
            Item(StraightTrack(direction: PlaybackDirection.PingPong), 1),
            Item(StraightTrack()),
        ]);
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
        var playback = new PlaylistPlayback([Item(StraightTrack(loop: true)), Item(StraightTrack())]);
        playback.Advance(1003f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ACountOverridesTheTracksLoop()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(loop: true), 1), Item(StraightTrack())]);
        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopCountBelowOneActsAsOne()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(loop: true), 0), Item(StraightTrack())]);
        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void AZeroLengthEntryIsShownForOneFrame()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(Snap(50f, 0f)), Item(StraightTrack())]);

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
        var playback = new PlaylistPlayback([Item(Snap(50f, 0f)), Item(StraightTrack())]);

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
        var playback = new PlaylistPlayback([Item(Snap(50f, 3f)), Item(StraightTrack())]);

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
        var playback = new PlaylistPlayback([Item(Snap(50f, 0f, loop: true)), Item(StraightTrack())]);
        playback.Advance(100f);
        Assert.Equal(0, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingStaysInTheCurrentLoopPass()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(), 2), Item(StraightTrack())]);
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
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);
        playback.Advance(25f);

        playback.Seek(3.0);

        Assert.False(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 4);
    }

    [Fact]
    public void RestartGoesBackToTheFirstEntry()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);
        playback.Advance(25f);

        playback.Restart();

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime);
        Assert.False(playback.IsFinished);
        Assert.Equal(10.0, playback.ShotLength, 4);
    }

    [Fact]
    public void ALoopingPlaylistWrapsToTheFirstEntryCarryingTimeOver()
    {
        var items = new[] { Item(StraightTrack()), Item(StraightTrack()) };
        var playback = new PlaylistPlayback(items, loops: true);

        playback.Advance(23f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALoopingPlaylistWrapsAtMostOncePerAdvance()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())], loops: true);

        playback.Advance(45f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void AnEntryThatHoldsThePlaylistStillHoldsWhenItLoops()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack(loop: true))], loops: true);

        playback.Advance(100f);

        Assert.Equal(1, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALoopingPlaylistOfZeroLengthEntriesShowsOneEntryPerAdvance()
    {
        var playback = new PlaylistPlayback([Item(Snap(0f, 0f)), Item(Snap(5f, 0f))], loops: true);

        playback.Advance(0.1f);
        var first = playback.Index;
        playback.Advance(0.1f);
        var second = playback.Index;
        playback.Advance(0.1f);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Equal(0, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingInTheLastEntryOfALoopingPlaylistNeverFinishesIt()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())], loops: true);
        playback.Advance(12f);

        playback.Seek(10.0);

        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingToTheEndOfALoopPassShowsItsLastFrameUntilPlaybackMovesOn()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(), 3), Item(StraightTrack())]);
        playback.Advance(3f);

        // Pass 1 of 3, L = 10: a seek to 10 is that pass's end, not pass 2's start.
        playback.Seek(10.0);
        Assert.Equal(0, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        playback.Advance(0f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);

        // Back into the same pass: 4 s into pass 1; 1 s on is 5 s, still entry 0.
        playback.Seek(4.0);
        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);

        // To the end again, then 1 s on: pass 2 at 1 s.
        playback.Seek(10.0);
        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);

        // The entry's clock is 11 of its 30: 18 s on is 29, still entry 0; 2 s more is 31, entry 1 at 1 s.
        // Had the seek skipped a pass (clock 21), the 18 s would already have cut.
        playback.Advance(18f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(9.0, playback.ShotTime, 4);
        playback.Advance(2f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingToTheEndOfAnEntryThatLoopsForGoodShowsItsLastFrame()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(loop: true)), Item(StraightTrack())]);

        // Clock 23 is 3 s into the third pass; a seek to 10 is that pass's end.
        playback.Advance(23f);
        playback.Seek(10.0);
        Assert.Equal(10.0, playback.ShotTime, 4);
        playback.Advance(0f);
        Assert.Equal(10.0, playback.ShotTime, 4);

        // 2 s on: the next pass at 2 s, same entry.
        playback.Advance(2f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingToTheEndOfAnEntryHoldsItUntilPlaybackMovesOn()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);
        playback.Advance(3f);

        playback.Seek(10.0);
        playback.Advance(0f);
        playback.Advance(0f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);

        // 1 s on: the cut to entry 1, carrying the second over.
        playback.Advance(1f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingToTheEndOfALoopingPlaylistHoldsItsLastEntryUntilPlaybackMovesOn()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())], loops: true);

        // 12 s is 2 s into entry 1, the last.
        playback.Advance(12f);
        playback.Seek(10.0);
        playback.Advance(0f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);

        // 1 s on: wraps to entry 0 at 1 s.
        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingAPingPongLoopPassToItsStartStaysOnItsReturnUntilPlaybackMovesOn()
    {
        var playback = new PlaylistPlayback([
            Item(StraightTrack(direction: PlaybackDirection.PingPong), 2),
            Item(StraightTrack()),
        ]);

        // Cycle 2 * 10 = 20. Clock 13 is on pass 1's return: shot time 20 - 13 = 7.
        playback.Advance(13f);
        Assert.Equal(7.0, playback.ShotTime, 4);

        // Return pass, shot time 0: pass clock 20, pass 1's end.
        playback.Seek(0.0);
        playback.Advance(0f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);

        // Still on pass 1's return: shot time 4 is pass clock 20 - 4 = 16; 1 s on, 17 is shot time 3.
        playback.Seek(4.0);
        playback.Advance(1f);
        Assert.Equal(3.0, playback.ShotTime, 4);

        // 4 s on: pass clock 21 is pass 2's outward run at 1 s, same entry.
        playback.Advance(4f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void RestartAfterASeekToAPassEndGoesBackToTheStart()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(loop: true)), Item(StraightTrack())]);
        playback.Advance(3f);
        playback.Seek(10.0);

        playback.Restart();

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime);
    }

    [Fact]
    public void AZeroLengthLoopingEntryStaysAtZeroAfterASeek()
    {
        var playback = new PlaylistPlayback([Item(Snap(0f, 0f, loop: true)), Item(StraightTrack())]);

        // No length: every seek clamps to 0 and there's no pass end to sit on.
        playback.Seek(5.0);
        playback.Advance(0.5f);
        playback.Advance(0.5f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime);
        Assert.False(playback.IsFinished);
    }

    // A single point at the origin, held 1 s, watching Guard with heavy smoothing.
    private static Track Watch() => WatchingGuard(smoothing: 1f, hold: 1f);

    [Fact]
    public void ACutOrASeekStartsTheSmoothingAfresh()
    {
        var characters = GuardAt(0f);
        var playback = new PlaylistPlayback([Item(Watch()), Item(Watch())], targets: characters);
        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(0.1f)!.Value, 3);

        GuardAt(characters, 10f);
        playback.Advance(0.5f);

        var cut = playback.Advance(0.5f)!.Value;
        Assert.Equal(1, playback.Index);
        AimsAt(new Vector3(10f, 0f, -10f), cut, 3);

        GuardAt(characters, -10f);
        playback.Seek(0.2);
        AimsAt(new Vector3(-10f, 0f, -10f), playback.Advance(0.01f)!.Value, 3);
    }

    [Fact]
    public void AWrapStartsTheSmoothingAfresh()
    {
        var characters = GuardAt(0f);
        var playback = new PlaylistPlayback([Item(Watch())], loops: true, targets: characters);
        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(0.1f)!.Value, 3);

        GuardAt(characters, 10f);
        var wrapped = playback.Advance(1f)!.Value;

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.1, playback.ShotTime, 4);
        AimsAt(new Vector3(10f, 0f, -10f), wrapped, 3);
    }

    [Fact]
    public void AZeroLengthEntryStartsTheSmoothingAfresh()
    {
        var characters = GuardAt(0f);
        var zero = WatchingGuard(smoothing: 1f);
        var playback = new PlaylistPlayback([Item(Watch()), Item(zero), Item(Watch())], targets: characters);
        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(0.1f)!.Value, 3);

        GuardAt(characters, 10f);
        var cut = playback.Advance(1f)!.Value;

        Assert.Equal(1, playback.Index);
        AimsAt(new Vector3(10f, 0f, -10f), cut, 3);
    }
}
