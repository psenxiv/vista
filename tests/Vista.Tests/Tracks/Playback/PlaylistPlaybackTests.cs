using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Playback;

public class PlaylistPlaybackTests
{
    // A single point at x held for the given seconds.
    private static Track Snap(float x, float hold, bool loop = false)
        => TrackEditing.SetHold(TrackEditing.SetLoop(TrackEditing.Append(TrackEditing.Empty(), Point(x)), loop), 0, hold);

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
        var playback = new PlaylistPlayback([Item(StraightTrack(direction: PlaybackDirection.PingPong), 1), Item(StraightTrack())]);
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

    // A single point at the origin, held 1 s, watching Guard with heavy smoothing.
    private static Track Watch() => TrackEditing.SetHold(TrackEditing.Append(TrackEditing.Empty(AimMode.WatchTarget), Point(0f)), 0, 1f) with { TargetName = "Guard", Smoothing = 1f };

    private static void GuardAt(NearbyCharacters characters, float x)
        => characters.Update([new LoadedCharacter("Guard", null, new Vector3(x, -1.3f, -10f))]);


    [Fact]
    public void ACutOrASeekStartsTheSmoothingAfresh()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
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
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
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
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
        var zero = TrackEditing.Append(TrackEditing.Empty(AimMode.WatchTarget), Point(0f)) with { TargetName = "Guard", Smoothing = 1f };
        var playback = new PlaylistPlayback([Item(Watch()), Item(zero), Item(Watch())], targets: characters);
        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(0.1f)!.Value, 3);

        GuardAt(characters, 10f);
        var cut = playback.Advance(1f)!.Value;

        Assert.Equal(1, playback.Index);
        AimsAt(new Vector3(10f, 0f, -10f), cut, 3);
    }
}
