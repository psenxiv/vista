using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class DirectorTests
{
    private static ControlPoint Point(float x, float y, float z)
        => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static Track StraightTrack(bool loop = false)
    {
        var track = TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x, 0f, 0f));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    [Fact]
    public void TickIsNullBeforeGoingLive()
    {
        var director = new Director();
        Assert.Null(director.Tick(1f / 60f));
    }

    [Fact]
    public void TickIsNullAfterGoingOffline()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.GoOffline();

        Assert.Null(director.Tick(1f / 60f));
    }

    [Fact]
    public void GoLiveTurnsLiveModeOnAndClearsPause()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        Assert.True(director.IsLive);
        Assert.False(director.IsPaused);
    }

    [Fact]
    public void GoOfflineTurnsLiveModeOffAndClearsPause()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Pause();

        director.GoOffline();

        Assert.False(director.IsLive);
        Assert.False(director.IsPaused);
    }

    [Fact]
    public void PauseOnlyTakesEffectWhileLive()
    {
        var director = new Director();
        director.Pause();

        Assert.False(director.IsPaused);
    }

    [Fact]
    public void PauseHoldsTheCurrentFrameOfATrackShot()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        director.Tick(5f);
        director.Pause();
        var held = director.Tick(1f);
        var heldAgain = director.Tick(2f);

        Assert.Equal(held, heldAgain);
    }

    [Fact]
    public void ResumeContinuesFromThePausedFrame()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        director.Tick(3f);
        director.Pause();
        director.Tick(2f);
        director.Resume();
        director.Tick(1f);

        Assert.False(director.IsPaused);
        Assert.Equal(4.0, director.ShotTime, 5);
    }

    [Fact]
    public void ResumeOnlyTakesEffectWhileLive()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Pause();
        director.GoOffline();

        director.Resume();

        Assert.False(director.IsLive);
        Assert.False(director.IsPaused);
    }

    [Fact]
    public void GoLiveRestartsATrackShotFromZero()
    {
        var director = new Director();
        var shot = new TrackShot(StraightTrack());
        director.GoLive(shot);
        director.Tick(5f);

        director.GoLive(shot);
        var frame = director.Tick(0f);
        var expected = new TrackEvaluator(shot.Track).Evaluate(0.0);

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void CallingGoLiveWhileLiveRestarts()
    {
        var director = new Director();
        var shot = new TrackShot(StraightTrack());
        director.GoLive(shot);
        director.Tick(5f);
        director.Pause();

        director.GoLive(shot);

        Assert.False(director.IsPaused);
        var frame = director.Tick(0f);
        var expected = new TrackEvaluator(shot.Track).Evaluate(0.0);
        Assert.Equal(expected, frame);
    }

    [Fact]
    public void TickOnATrackShotAdvancesThePlayback()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        var frame = director.Tick(5f);

        var expected = new TrackEvaluator(StraightTrack()).Evaluate(5.0);
        Assert.Equal(expected, frame);
    }

    [Fact]
    public void TickOnAGameCameraShotIsAlwaysNull()
    {
        var director = new Director();
        director.GoLive(new GameCameraShot());

        Assert.Null(director.Tick(1f / 60f));
        Assert.Null(director.Tick(1f));
    }

    [Fact]
    public void TickOnATrackWithNoPointsIsNull()
    {
        var empty = TrackEditing.Empty();
        var director = new Director();
        director.GoLive(new TrackShot(empty));

        Assert.Null(director.Tick(1f / 60f));
    }

    [Fact]
    public void IsFinishedIsFalseUntilATrackThatDoesNotLoopReachesItsEnd()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        Assert.False(director.IsFinished);
        director.Tick(5f);
        Assert.False(director.IsFinished);
        director.Tick(20f);
        Assert.True(director.IsFinished);
    }

    [Fact]
    public void ShotTimeIsZeroBeforeGoingLive()
    {
        Assert.Equal(0.0, new Director().ShotTime);
    }

    [Fact]
    public void ShotTimeFollowsATrackShotsPlayback()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        director.Tick(2f);
        director.Tick(1.5f);

        Assert.Equal(3.5, director.ShotTime, 5);
    }

    [Fact]
    public void ShotTimeStopsWhilePaused()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Tick(2f);

        director.Pause();
        director.Tick(3f);

        Assert.Equal(2.0, director.ShotTime, 5);
    }

    [Fact]
    public void ShotTimeRestartsOnGoLive()
    {
        var director = new Director();
        var shot = new TrackShot(StraightTrack());
        director.GoLive(shot);
        director.Tick(4f);

        director.GoLive(shot);

        Assert.Equal(0.0, director.ShotTime);
    }

    [Fact]
    public void GoLiveWithATrackThatCannotPlayLeavesTheCurrentShotOnProgram()
    {
        var director = new Director();
        director.GoLive(new GameCameraShot());
        var broken = StraightTrack() with { Timing = [] };

        Assert.Throws<ArgumentException>(() => director.GoLive(new TrackShot(broken)));

        Assert.True(director.IsLive);
        Assert.Null(director.Tick(1f));
    }

    [Fact]
    public void SeekMovesALiveTrackAndKeepsItsPause()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Pause();

        director.Seek(6.0);
        Assert.Equal(6.0, director.ShotTime, 5);
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void SeekDoesNothingOffline()
    {
        var director = new Director();
        director.Seek(3.0);
        Assert.Equal(0.0, director.ShotTime);
    }

    [Fact]
    public void APlaylistShotPlaysItsEntriesInTurn()
    {
        var items = new[] { new PlaylistItem(Guid.NewGuid(), StraightTrack(), null), new PlaylistItem(Guid.NewGuid(), StraightTrack(), null) };
        var director = new Director();
        director.GoLive(new PlaylistShot(items));

        director.Tick(12f);

        Assert.Equal(items[1].EntryId, director.Playlist!.EntryId);
        Assert.Equal(2.0, director.ShotTime, 4);
        Assert.Equal(10.0, director.ShotLength, 4);
        director.Seek(5.0);
        Assert.Equal(5.0, director.ShotTime, 4);
        director.Tick(10f);
        Assert.True(director.IsFinished);
    }

    [Fact]
    public void ATrackShotHasNoPlaylist()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        Assert.Null(director.Playlist);
        Assert.Equal(10.0, director.ShotLength, 4);
    }
}
