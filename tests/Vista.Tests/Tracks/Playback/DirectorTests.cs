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
    // Seconds through a shot, as the playhead counts them from float frame steps.
    private const double HeadTolerance = 1e-5;

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
    public void ScrubToMovesALiveShotAndKeepsItsPause()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Pause();

        director.ScrubTo(6.0);
        Assert.Equal(6.0, director.ShotTime, 5);
        Assert.Equal(6.0, director.Head, HeadTolerance);
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void ScrubToDoesNothingOffline()
    {
        var director = new Director();
        director.ScrubTo(3.0);
        Assert.Equal(0.0, director.ShotTime);
        Assert.Equal(0.0, director.Head);
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

    // StraightTrack's control points sit at x = 0, 5 and 10 with two legs of 5 s, so the shot runs
    // 10 s. The points are collinear and evenly spaced, so the spline is the straight line through
    // them and arc length along it is x. Both timing secants are 5 yalms / 5 s = 1, so PCHIP gives
    // every key a tangent of 1 and the distance curve is d(t) = t. Position at shot time t is
    // therefore exactly x = t, and every value below reads straight off PlaybackClock.ShotTime.
    private static float XAfter(Director director, float dt) => director.Tick(dt)!.Value.Position.X;

    [Fact]
    public void ReverseStartsAtTheEndAndRunsBackToTheStart()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack(direction: PlaybackDirection.Reverse))]));

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
    public void ScrubToTakesATimeThroughThePlaylist()
    {
        var items = new[] { Item(StraightTrack()), Item(StraightTrack()) };
        var director = new Director();
        director.GoLive(new PlaylistShot(items));

        // Two 10 s entries, 20 s in all: 13 s through is 3 s into the second.
        director.ScrubTo(13.0);

        Assert.Equal(20.0, director.Timeline!.Total, HeadTolerance);
        Assert.Equal(items[1].EntryId, director.Playlist!.EntryId);
        Assert.Equal(1, director.EntryIndex);
        Assert.Equal(3.0, director.ShotTime, 5);
        Assert.Equal(13.0, director.Head, HeadTolerance);
    }

    [Fact]
    public void TheScrubBarsReadsAreEmptyBeforeGoingLive()
    {
        var director = new Director();

        Assert.Null(director.Timeline);
        Assert.Equal(0.0, director.Head);
        Assert.Equal(0, director.EntryIndex);
        Assert.False(director.Scrubbing);
        Assert.False(director.IsPlaying);
    }

    [Fact]
    public void IsPlayingOnlyWhileLiveUnpausedAndUnfinished()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        Assert.True(director.IsPlaying);

        director.Pause();
        Assert.False(director.IsPlaying);

        director.Resume();
        Assert.True(director.IsPlaying);

        // The track runs 10 s, so 20 s plays it to its end.
        director.Tick(20f);
        Assert.False(director.IsPlaying);

        director.GoOffline();
        Assert.False(director.IsPlaying);
    }

    [Fact]
    public void PlayResumesAPausedShot()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Tick(3f);
        director.Pause();

        director.Play();

        Assert.False(director.IsPaused);
        // On from 3 s, 1 s more is 4 s.
        director.Tick(1f);
        Assert.Equal(4.0, director.Head, HeadTolerance);
    }

    [Fact]
    public void PlayRestartsAFinishedShot()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        // The track runs 10 s, so 20 s plays it to its end.
        director.Tick(20f);
        Assert.True(director.IsFinished);

        director.Play();

        Assert.False(director.IsFinished);
        Assert.Equal(0.0, director.Head);
        // From the start, 2 s on is x = 2.
        Assert.Equal(2f, XAfter(director, 2f), 1e-3f);
    }

    [Fact]
    public void PlayDoesNothingOffline()
    {
        var director = new Director();
        director.Play();
        Assert.False(director.IsLive);
    }

    [Fact]
    public void BeginScrubHoldsTheShot()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Tick(3f);

        director.BeginScrub();

        Assert.True(director.Scrubbing);
        Assert.True(director.IsPaused);
        // Held at 3 s, so 2 s of ticking moves nothing.
        director.Tick(2f);
        Assert.Equal(3.0, director.Head, HeadTolerance);
    }

    [Fact]
    public void BeginScrubDoesNothingOffline()
    {
        var director = new Director();
        director.BeginScrub();
        Assert.False(director.Scrubbing);
    }

    [Fact]
    public void EndScrubResumesAShotThatWasUnpaused()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Tick(3f);

        director.BeginScrub();
        director.ScrubTo(6.0);
        director.EndScrub();

        Assert.False(director.Scrubbing);
        Assert.True(director.IsPlaying);
        // Let go at 6 s, 1 s on is x = 7.
        Assert.Equal(7f, XAfter(director, 1f), 1e-3f);
    }

    [Fact]
    public void EndScrubLeavesAPausedShotPaused()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        director.Pause();

        director.BeginScrub();
        director.ScrubTo(6.0);
        director.EndScrub();

        Assert.False(director.Scrubbing);
        Assert.True(director.IsPaused);
        // Still held where it was let go.
        director.Tick(1f);
        Assert.Equal(6.0, director.Head, HeadTolerance);
    }

    [Fact]
    public void ASecondBeginScrubKeepsWhatTheFirstRemembered()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));

        // The first scrub pauses a playing shot; a second begun then must not read that pause as the shot's own.
        director.BeginScrub();
        director.BeginScrub();
        director.EndScrub();

        Assert.True(director.IsPlaying);
    }

    [Fact]
    public void AFinishedShotScrubbedBackAndLetGoPlaysOn()
    {
        var director = new Director();
        director.GoLive(new PlaylistShot([Item(StraightTrack())]));
        // The track runs 10 s, so 20 s plays it to its end.
        director.Tick(20f);
        Assert.True(director.IsFinished);

        director.BeginScrub();
        director.ScrubTo(3.0);
        director.EndScrub();

        Assert.True(director.IsPlaying);
        // Let go at 3 s, 1 s on is x = 4.
        Assert.Equal(4f, XAfter(director, 1f), 1e-3f);
    }

    [Fact]
    public void GoLiveEndsAScrub()
    {
        var director = new Director();
        var shot = new PlaylistShot([Item(StraightTrack())]);
        director.GoLive(shot);
        director.BeginScrub();

        director.GoLive(shot);

        Assert.False(director.Scrubbing);
        // The new shot's own pause isn't undone by letting go of the old scrub.
        director.Pause();
        director.EndScrub();
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void GoOfflineEndsAScrub()
    {
        var director = new Director();
        var shot = new PlaylistShot([Item(StraightTrack())]);
        director.GoLive(shot);
        director.BeginScrub();

        director.GoOffline();

        Assert.False(director.Scrubbing);
        // A shot put on afterwards and paused isn't resumed by letting go of the old scrub.
        director.GoLive(shot);
        director.Pause();
        director.EndScrub();
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void RestartPlaysTheShotFromItsStartUnpaused()
    {
        var items = new[] { Item(StraightTrack()), Item(StraightTrack()) };
        var director = new Director();
        director.GoLive(new PlaylistShot(items));
        director.Tick(25f);
        director.Pause();

        director.Restart();

        Assert.Equal(items[0].EntryId, director.Playlist!.EntryId);
        Assert.Equal(0.0, director.ShotTime);
        Assert.False(director.IsPaused);
        Assert.False(director.IsFinished);
    }

    [Fact]
    public void RestartDoesNothingOffline()
    {
        var director = new Director();
        director.Restart();
        Assert.False(director.IsLive);
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
                    GoLive(state);
                    var board = state.Board!;
                    AssertEveryFrameWellFormed(
                        steps,
                        FrameBudget,
                        dt => board.IsFinished ? null : new Played(state.LiveFrame(dt), board.Head)
                    );
                },
                iter: 500,
                print: Kept<(Scene Scene, float[] Steps)>(x =>
                    $"{SceneJson.Write(x.Scene)}\nSteps: {string.Join(", ", x.Steps)}"
                )
            );
    }
}
