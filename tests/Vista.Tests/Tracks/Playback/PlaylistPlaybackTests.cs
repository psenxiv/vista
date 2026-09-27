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

    [Fact]
    public void ThePlaylistTimeRunsThroughEveryPassAndEntry()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(), 3), Item(StraightTrack())]);

        // Segments of 10 * 3 = 30 s at 0 and 10 s at 30. Clock 23 is 3 s into pass 2 of entry 0.
        playback.Advance(23f);
        AssertPosition(new PlaylistPosition(0, 2, 3.0), playback.Position, 1e-4);
        Assert.Equal(23.0, playback.PlaylistTime, 4);

        // 9 s on: 32 is entry 1 at 2 s.
        playback.Advance(9f);
        AssertPosition(new PlaylistPosition(1, 0, 2.0), playback.Position, 1e-4);
        Assert.Equal(32.0, playback.PlaylistTime, 4);

        // Finished, the head sits at the end: 40.
        playback.Advance(20f);
        Assert.True(playback.IsFinished);
        AssertPosition(new PlaylistPosition(1, 0, 10.0), playback.Position, 1e-4);
        Assert.Equal(40.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void ThePlaylistTimeRunsForwardWhicheverWayTheTrackRuns()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(direction: PlaybackDirection.Reverse))]);

        // 3 s in, a Reverse track is at shot time 10 - 3 = 7, but 3 s through the playlist.
        playback.Advance(3f);

        Assert.Equal(7.0, playback.ShotTime, 4);
        Assert.Equal(3.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void ALoopingPlaylistsTimeWrapsToTheStart()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())], loops: true);

        // 23 s round a 20 s playlist is 3 s.
        playback.Advance(23f);

        Assert.Equal(3.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void ThePlaylistTimeGoesRoundAnEntryLoopingForever()
    {
        var playback = new PlaylistPlayback([
            Item(StraightTrack()),
            Item(StraightTrack(loop: true)),
            Item(StraightTrack()),
        ]);

        // The forever entry's segment is 10 to 20. Clock 45 is 35 s into it: 5 s into a pass, so 15.
        playback.Advance(45f);

        Assert.Equal(1, playback.Index);
        AssertPosition(new PlaylistPosition(1, 0, 5.0), playback.Position, 1e-4);
        Assert.Equal(15.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void SeekingByPlaylistTimeCutsToTheEntryPassAndTimeThere()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack(), 2), Item(StraightTrack())]);

        // Segments at 0, 10 (two 10 s passes) and 30. 25 is 15 s into entry 1: pass 1 at 5 s.
        playback.SeekPlaylist(25.0);
        AssertPosition(new PlaylistPosition(1, 1, 5.0), playback.Position, 1e-4);
        Assert.Equal(5.0, playback.ShotTime, 4);

        // 1 s on plays on from there: 26.
        playback.Advance(1f);
        Assert.Equal(26.0, playback.PlaylistTime, 4);

        // 10 s more is 36: pass 1 ended at 30, so entry 2 at 6 s.
        playback.Advance(10f);
        Assert.Equal(2, playback.Index);
        Assert.Equal(6.0, playback.ShotTime, 4);

        // Back to entry 0.
        playback.SeekPlaylist(4.0);
        Assert.Equal(0, playback.Index);
        Assert.Equal(4.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingToAnEntrysEndLandsInTheNextSegment()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);

        // 10 is entry 0's end and entry 1's start.
        playback.SeekPlaylist(10.0);
        playback.Advance(0f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingByPlaylistTimeIntoAPingPongPassFindsItsReturn()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack(direction: PlaybackDirection.PingPong), 2)]);

        // Passes of 2 * 10 = 20 s. 35 is pass 1 at 15, on the return: shot time 20 - 15 = 5.
        playback.SeekPlaylist(35.0);

        AssertPosition(new PlaylistPosition(0, 1, 15.0), playback.Position, 1e-4);
        Assert.Equal(5.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingToTheVeryEndHoldsTheLastFrame()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())]);
        playback.Advance(3f);

        // The playlist is 20 s: its end is entry 1's last frame, and playback has finished.
        playback.SeekPlaylist(20.0);
        Assert.True(playback.IsFinished);
        playback.Advance(1f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.Equal(20.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void SeekingToTheEndOfALoopingPlaylistHoldsThereThenWraps()
    {
        var playback = new PlaylistPlayback([Item(StraightTrack()), Item(StraightTrack())], loops: true);

        playback.SeekPlaylist(20.0);
        playback.Advance(0f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);

        // 1 s on: wraps to entry 0 at 1 s.
        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(1.0, playback.PlaylistTime, 4);
    }

    [Fact]
    public void SeekingToTheEndOfAnEntryLoopingForeverHoldsItsLastFrameThenLoopsIt()
    {
        var playback = new PlaylistPlayback([
            Item(StraightTrack()),
            Item(StraightTrack(loop: true)),
            Item(StraightTrack()),
        ]);

        // The timeline ends at 20 with the forever entry; past the end is its end too.
        playback.SeekPlaylist(99.0);
        playback.Advance(0f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.Equal(20.0, playback.PlaylistTime, 4);
        Assert.False(playback.IsFinished);

        // 2 s on: its next pass at 2 s, so 12 through the playlist.
        playback.Advance(2f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
        Assert.Equal(12.0, playback.PlaylistTime, 4);
    }

    // The same entry and pass, and the time within the tolerance.
    private static void AssertPosition(PlaylistPosition expected, PlaylistPosition actual, double tolerance)
    {
        Assert.Equal(expected.Index, actual.Index);
        Assert.Equal(expected.Pass, actual.Pass);
        Assert.Equal(expected.Time, actual.Time, tolerance);
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

        GuardAt(characters, 10f);
        playback.SeekPlaylist(0.2);
        AimsAt(new Vector3(10f, 0f, -10f), playback.Advance(0.01f)!.Value, 3);
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

    [Fact]
    public void ThePositionCountsWholePassesAndTheTimeIntoThisOne()
    {
        // A 2 s entry played three times: 3.5 s in is the second pass (from 0, pass 1), 1.5 s into it.
        var playback = new PlaylistPlayback([Item(OneLeg(2f), 3)]);

        playback.Advance(3.5f);

        Assert.Equal(new PlaylistPosition(0, 1, 1.5), playback.Position);
    }
}
