using System.Numerics;
using CsCheck;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;
using static Vista.Tests.Tracks.Playback.PlaybackFixtures;

namespace Vista.Tests.Tracks.Playback;

public class DirectorTests
{
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
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.GoOffline();

        Assert.Null(director.Tick(1f / 60f));
    }

    [Fact]
    public void GoLiveTurnsLiveModeOnAndClearsPause()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

        Assert.True(director.IsLive);
        Assert.False(director.IsPaused);
    }

    [Fact]
    public void GoOfflineTurnsLiveModeOffAndClearsPause()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
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
    public void PauseHoldsTheCurrentFrame()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

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
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

        director.Tick(3f);
        director.Pause();
        director.Tick(2f);
        director.Resume();
        director.Tick(1f);

        Assert.False(director.IsPaused);
        Assert.Equal(4.0, director.ShotTime, 5);
    }

    [Fact]
    public void GoLiveRestartsFromZero()
    {
        var director = new Director();
        var shot = new PlaylistShot([Item(StraightTrack())]);
        director.GoLive(shot);
        director.Tick(5f);

        director.GoLive(shot);
        var frame = director.Tick(0f)!.Value;

        // Restarted, so shot time 0: x = t puts the camera at the origin. See StraightTrackFrame.
        StraightTrackFrame(0f, frame);
    }

    [Fact]
    public void TickAdvancesThePlayback()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

        var frame = director.Tick(5f)!.Value;

        StraightTrackFrame(5f, frame);
    }

    // StraightTrack at shot time t: position (t, 0, 0) by the x = t derivation further down. Its aim
    // is PathTangent along a straight +x line, so it looks along +x, and every point has FoV 1 and
    // roll 0, so those hold throughout.
    private static void StraightTrackFrame(float t, Vista.Core.Camera.CameraState frame)
    {
        Near(new Vector3(t, 0f, 0f), frame.Position, 1e-3f);
        Near(Vector3.UnitX, frame.Forward, 1e-3f);
        Assert.Equal(1f, frame.Fov, 1e-5f);
        Assert.Equal(0f, frame.Roll, 1e-5f);
    }

    [Fact]
    public void TickOnATrackWithNoPointsIsNull()
    {
        var empty = TrackEditing.Empty();
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(empty)]));

        Assert.Null(director.Tick(1f / 60f));
    }

    [Fact]
    public void IsFinishedIsFalseUntilATrackThatDoesNotLoopReachesItsEnd()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

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
    public void ShotTimeFollowsThePlayback()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

        director.Tick(2f);
        director.Tick(1.5f);

        Assert.Equal(3.5, director.ShotTime, 5);
    }

    [Fact]
    public void ShotTimeStopsWhilePaused()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Tick(2f);

        director.Pause();
        director.Tick(3f);

        Assert.Equal(2.0, director.ShotTime, 5);
    }

    [Fact]
    public void GoLiveWithATrackThatCannotPlayLeavesTheCurrentShotOnProgram()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        var broken = StraightTrack() with { Timing = [] };

        Assert.Throws<ArgumentException>(() => director.GoLive(new PlaylistShot([Item(broken)])));

        Assert.True(director.IsLive);
        // Unchanged, so still the original track at shot time 0.
        StraightTrackFrame(0f, director.Tick(0f)!.Value);
    }

    [Fact]
    public void SeekMovesALiveTrackAndKeepsItsPause()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
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
        var items = new[]
        {
            new PlaylistItem(Guid.NewGuid(), StraightTrack(), null),
            new PlaylistItem(Guid.NewGuid(), StraightTrack(), null),
        };
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
    public void LiveWatchesACharacterTheDirectorWasGiven()
    {
        var director = new Director(GuardAt(0f));
        director.GoLive(new PlaylistShot([Item(WatchingGuard())]));

        AimsAt(new Vector3(0f, 0f, -10f), director.Tick(1f / 60f)!.Value, 4);
    }

    [Fact]
    public void APausedLiveTickHoldsTheEasedAimAfterTheCharacterMoves()
    {
        var characters = GuardAt(0f);
        var director = new Director(characters);
        director.GoLive(new PlaylistShot([Item(WatchingGuard(smoothing: 1f))]));
        director.Tick(1f / 60f);
        GuardAt(characters, 10f);
        var eased = director.Tick(0.5f)!.Value;

        director.Pause();
        GuardAt(characters, -20f);
        var held = director.Tick(1f / 60f)!.Value;

        Near(eased.Forward, held.Forward, 5e-5f);
    }

    private static float XAfter(Director director, float dt) => director.Tick(dt)!.Value.Position.X;

    [Fact]
    public void ReverseStartsAtTheEndAndRunsBackToTheStart()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack(direction: PlaybackDirection.Reverse))]));
        Assert.Equal(10.0, director.ShotLength, 5);

        // Shot time is length - clock, so clocks 0, 2.5 and 5 give 10, 7.5 and 5.
        Assert.Equal(10f, XAfter(director, 0f), 3);
        Assert.Equal(7.5f, XAfter(director, 2.5f), 3);
        Assert.Equal(5f, XAfter(director, 2.5f), 3);

        // The clock stops at the cycle, so an overrun lands on shot time 0.
        Assert.Equal(0f, XAfter(director, 20f), 3);
        Assert.True(director.IsFinished);
    }

    [Fact]
    public void PingPongRunsOutAndBackOverTwiceTheLength()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack(direction: PlaybackDirection.PingPong))]));

        // Shot time is the clock up to the length, then 2 * length - clock. The cycle is 20 s.
        Assert.Equal(0f, XAfter(director, 0f), 3);
        Assert.Equal(5f, XAfter(director, 5f), 3);
        Assert.Equal(10f, XAfter(director, 5f), 3);
        Assert.Equal(7.5f, XAfter(director, 2.5f), 3);
        Assert.Equal(0f, XAfter(director, 7.5f), 3);
    }

    [Fact]
    public void PingPongIsNotFinishedAtTheTurnaround()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack(direction: PlaybackDirection.PingPong))]));

        director.Tick(10f);
        Assert.Equal(10.0, director.ShotTime, 5);
        Assert.False(director.IsFinished);

        director.Tick(10f);
        Assert.Equal(0.0, director.ShotTime, 5);
        Assert.True(director.IsFinished);
    }

    [Fact]
    public void SeekTakesAShotTimeWhicheverWayTheTrackRuns()
    {
        var forward = new Director();
        forward.GoLive(new PlaylistShot([Item(StraightTrack())]));
        forward.Seek(2.5);

        var reverse = new Director();
        reverse.GoLive(new PlaylistShot([Item(StraightTrack(direction: PlaybackDirection.Reverse))]));
        reverse.Seek(2.5);

        // Both land on shot time 2.5 and so on x = 2.5, though Reverse's clock behind it is 7.5.
        Assert.Equal(2.5, forward.ShotTime, 5);
        Assert.Equal(2.5, reverse.ShotTime, 5);
        Assert.Equal(2.5f, XAfter(forward, 0f), 3);
        Assert.Equal(2.5f, XAfter(reverse, 0f), 3);
    }

    /// <summary>The most frames a live playlist is ticked for.</summary>
    private const int FrameBudget = 600;

    [Fact]
    [Trait("Category", "Property")]
    public void EveryLiveFrameIsWellFormed()
    {
        Gen.Select(AnyPlaylistScene, AnyFrameStep.Array[1, 32])
            .Sample(
                (scene, steps) =>
                {
                    var state = new SessionState();
                    state.LoadScene(scene);
                    state.Restart();
                    var director = state.Director;
                    AssertEveryFrameWellFormed(
                        steps,
                        FrameBudget,
                        dt => director.IsFinished ? null : new Played(director.Tick(dt), director.ShotTime)
                    );
                },
                iter: 500,
                print: Kept<(Scene Scene, float[] Steps)>(x =>
                    $"{SceneJson.Write(x.Scene)}\nSteps: {string.Join(", ", x.Steps)}"
                )
            );
    }
}
