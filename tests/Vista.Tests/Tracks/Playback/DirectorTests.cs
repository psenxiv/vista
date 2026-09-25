using System.Numerics;
using CsCheck;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
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
        var frame = director.Tick(0f)!.Value;

        // Restarted, so shot time 0: x = t puts the camera at the origin. See StraightTrackFrame.
        StraightTrackFrame(0f, frame);
    }

    [Fact]
    public void TickOnATrackShotAdvancesThePlayback()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));

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
    public void ATrackShotHasNoPlaylist()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        Assert.Null(director.Playlist);
        Assert.Equal(10.0, director.ShotLength, 4);
    }

    [Fact]
    public void GoLiveWithTheGameCameraClearsAFinishedTracksState()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Tick(20f);
        Assert.True(director.IsFinished);

        director.GoLive(new GameCameraShot());

        Assert.False(director.IsFinished);
        Assert.Equal(0.0, director.ShotTime);
        Assert.Equal(0.0, director.ShotLength);
        Assert.Null(director.Playlist);
        Assert.Null(director.Tick(1f));
    }

    [Fact]
    public void LiveWatchesACharacterTheDirectorWasGiven()
    {
        var director = new Director(GuardAt(0f));
        director.GoLive(new TrackShot(WatchingGuard()));

        AimsAt(new Vector3(0f, 0f, -10f), director.Tick(1f / 60f)!.Value, 4);
    }

    [Fact]
    public void APausedLiveTickHoldsTheEasedAimAfterTheCharacterMoves()
    {
        var characters = GuardAt(0f);
        var director = new Director(characters);
        director.GoLive(new TrackShot(WatchingGuard(smoothing: 1f)));
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
        director.GoLive(new TrackShot(StraightTrack(direction: PlaybackDirection.Reverse)));
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
        director.GoLive(new TrackShot(StraightTrack(direction: PlaybackDirection.PingPong)));

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
        director.GoLive(new TrackShot(StraightTrack(direction: PlaybackDirection.PingPong)));

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
        forward.GoLive(new TrackShot(StraightTrack()));
        forward.Seek(2.5);

        var reverse = new Director();
        reverse.GoLive(new TrackShot(StraightTrack(direction: PlaybackDirection.Reverse)));
        reverse.Seek(2.5);

        // Both land on shot time 2.5 and so on x = 2.5, though Reverse's clock behind it is 7.5.
        Assert.Equal(2.5, forward.ShotTime, 5);
        Assert.Equal(2.5, reverse.ShotTime, 5);
        Assert.Equal(2.5f, XAfter(forward, 0f), 3);
        Assert.Equal(2.5f, XAfter(reverse, 0f), 3);
    }

    /// <summary>A generated path track, looping or not, played any way round.</summary>
    private static readonly Gen<Track> AnyPlayedTrack =
        from track in AnyPathTrack
        from loop in Gen.Bool
        from direction in Gen.Enum<PlaybackDirection>()
        select TrackEditing.SetDirection(TrackEditing.SetLoop(track, loop), direction);

    /// <summary>One to three generated tracks and a playlist of one to five of them, each playing once, repeated or following its track, looping or not, built as the Playlist panel builds it.</summary>
    private static readonly Gen<Scene> AnyPlaylistScene =
        from tracks in AnyPlayedTrack.Array[1, 3]
        // Per entry: which track, and 0 to follow the track or a repeat count.
        from entries in Gen.Select(Gen.Int[0, tracks.Length - 1], Gen.Int[0, 3]).Array[1, 5]
        from loops in Gen.Bool
        select PlaylistScene(tracks, entries, loops);

    private static Scene PlaylistScene(Track[] tracks, (int Track, int Loops)[] entries, bool loops)
    {
        var scene = new Scene(tracks, new HashSet<Guid>(), []);
        foreach (var (track, count) in entries)
        {
            scene = PlaylistEditing.Add(scene, [tracks[track].Id]);
            scene = PlaylistEditing.SetLoops(scene, scene.Playlist[^1].Id, count == 0 ? null : count);
        }

        return PlaylistEditing.SetPlaylistLoops(scene, loops);
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
                    for (var i = 0; i < FrameBudget && !state.Director.IsFinished; i++)
                        if (state.Director.Tick(steps[i % steps.Length]) is { } frame)
                            AssertWellFormed(frame, $"Frame {i} at {state.Director.ShotTime:0.######} s");
                },
                iter: 500,
                print: Kept<(Scene Scene, float[] Steps)>(x =>
                    $"{SceneJson.Write(x.Scene)}\nSteps: {string.Join(", ", x.Steps)}"
                )
            );
    }
}
