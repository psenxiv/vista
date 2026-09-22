using System.Numerics;
using Vista.Core.Camera;
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

    private static SnapPoint Snap()
        => new(new Vector3(1f, 2f, 3f), 0.4f, 0.1f, 1.2f);

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
        Assert.Equal(4.0, director.Elapsed, 5);
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
    public void TickOnASnapShotReturnsItsPoseEveryFrame()
    {
        var snap = Snap();
        var director = new Director();
        director.GoLive(new SnapShot(snap));

        var expected = new CameraState(snap.Position, FreeCamMotion.LookAtFrom(snap.Position, snap.Yaw, snap.Pitch), snap.Fov);

        Assert.Equal(expected, director.Tick(1f));
        Assert.Equal(expected, director.Tick(100f));
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
    public void IsFinishedIsFalseUntilAOnceTrackReachesItsEnd()
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
    public void IsFinishedIsFalseForNonTrackShots()
    {
        var director = new Director();
        director.GoLive(new SnapShot(Snap()));

        director.Tick(100f);

        Assert.False(director.IsFinished);
    }
    [Fact]
    public void ElapsedIsZeroBeforeGoingLive()
    {
        Assert.Equal(0.0, new Director().Elapsed);
    }

    [Fact]
    public void ElapsedFollowsATrackShotsPlayback()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

        director.Tick(2f);
        director.Tick(1.5f);

        Assert.Equal(3.5, director.Elapsed, 5);
    }

    [Fact]
    public void ElapsedStopsWhilePaused()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Tick(2f);

        director.Pause();
        director.Tick(3f);

        Assert.Equal(2.0, director.Elapsed, 5);
    }

    [Fact]
    public void ElapsedRestartsOnGoLive()
    {
        var director = new Director();
        var shot = new TrackShot(StraightTrack());
        director.GoLive(shot);
        director.Tick(4f);

        director.GoLive(shot);

        Assert.Equal(0.0, director.Elapsed);
    }

    [Fact]
    public void ElapsedIsZeroForNonTrackShots()
    {
        var director = new Director();
        director.GoLive(new SnapShot(Snap()));

        director.Tick(3f);

        Assert.Equal(0.0, director.Elapsed);
    }

    [Fact]
    public void GoLiveWithATrackThatCannotPlayLeavesTheCurrentShotOnProgram()
    {
        var snap = Snap();
        var director = new Director();
        director.GoLive(new SnapShot(snap));
        var broken = StraightTrack() with { Timing = [] };

        Assert.Throws<ArgumentException>(() => director.GoLive(new TrackShot(broken)));

        var expected = new CameraState(snap.Position, FreeCamMotion.LookAtFrom(snap.Position, snap.Yaw, snap.Pitch), snap.Fov);
        Assert.True(director.IsLive);
        Assert.Equal(expected, director.Tick(1f));
    }

    [Fact]
    public void ASnapShotCarriesItsRoll()
    {
        var director = new Director();
        director.GoLive(new SnapShot(new SnapPoint(Vector3.Zero, 0f, 0f, 1f, 0.4f)));

        Assert.Equal(0.4f, director.Tick(0.1f)!.Value.Roll);
    }

    [Fact]
    public void SeekMovesALiveTrackAndKeepsItsPause()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Pause();

        director.Seek(6.0);
        Assert.Equal(6.0, director.Elapsed, 5);
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void SeekDoesNothingOfflineOrForSnapShots()
    {
        var director = new Director();
        director.Seek(3.0);
        Assert.Equal(0.0, director.Elapsed);

        director.GoLive(new SnapShot(Snap()));
        director.Seek(3.0);
        Assert.Equal(0.0, director.Elapsed);
    }
}
