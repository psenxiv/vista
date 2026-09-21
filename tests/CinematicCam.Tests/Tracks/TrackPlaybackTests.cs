using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class TrackPlaybackTests
{
    private static ControlPoint Point(float x, float y, float z)
        => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static TimingKey Key(float time, float position)
        => new(time, position, TangentMode.Auto, 0f, 0f);

    private static Track StraightTrack(PlaybackMode mode)
    {
        var points = new[] { Point(0f, 0f, 0f), Point(5f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[] { Key(0f, 0f), Key(10f, 2f) };
        return new Track(points, timing, AimMode.PathTangent, mode);
    }

    [Fact]
    public void PositionAtFiveSecondsMatchesUnder60FpsAnd30FpsDeltaSequences()
    {
        var sixty = new TrackPlayback(StraightTrack(PlaybackMode.Once));
        var thirty = new TrackPlayback(StraightTrack(PlaybackMode.Once));

        CameraState? last60 = null;
        for (var i = 0; i < 300; i++)
            last60 = sixty.Advance(1f / 60f);

        CameraState? last30 = null;
        for (var i = 0; i < 150; i++)
            last30 = thirty.Advance(1f / 30f);

        Assert.Equal(5.0, sixty.Elapsed, 3);
        Assert.Equal(5.0, thirty.Elapsed, 3);
        Assert.NotNull(last60);
        Assert.NotNull(last30);
        Assert.Equal(last60!.Value.Position.X, last30!.Value.Position.X, 3);
        Assert.Equal(last60.Value.Position.Y, last30.Value.Position.Y, 3);
        Assert.Equal(last60.Value.Position.Z, last30.Value.Position.Z, 3);
        Assert.Equal(last60.Value.Fov, last30.Value.Fov, 3);
    }

    [Fact]
    public void AFinishedOnceTrackHoldsItsLastFrame()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Once));

        var atEnd = playback.Advance(15f);
        Assert.True(playback.IsFinished);
        Assert.Equal(10.0, playback.Elapsed);

        var afterEnd = playback.Advance(5f);
        Assert.True(playback.IsFinished);
        Assert.Equal(10.0, playback.Elapsed);
        Assert.Equal(atEnd, afterEnd);
    }

    [Fact]
    public void ALoopTrackCutsBackToItsFirstFrameAfterItsLastKey()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Loop));

        var wrapped = playback.Advance(10f); // exactly the duration: wraps to elapsed 0
        Assert.False(playback.IsFinished);
        Assert.Equal(0.0, playback.Elapsed, 5);

        var evaluator = new TrackEvaluator(StraightTrack(PlaybackMode.Loop));
        var firstFrame = evaluator.Evaluate(0.0);
        Assert.Equal(firstFrame, wrapped);
    }

    [Fact]
    public void ALoopTrackNeverFinishesPastItsDuration()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Loop));

        playback.Advance(12f);
        Assert.False(playback.IsFinished);
        Assert.Equal(2.0, playback.Elapsed, 3);
    }

    [Fact]
    public void RestartResetsElapsedAndClearsIsFinished()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Once));
        playback.Advance(20f);
        Assert.True(playback.IsFinished);

        playback.Restart();
        Assert.Equal(0.0, playback.Elapsed);
        Assert.False(playback.IsFinished);
    }

    [Theory]
    [InlineData(PlaybackMode.Once)]
    [InlineData(PlaybackMode.Loop)]
    public void NegativeFrameTimeDoesNotRunTimeBackwards(PlaybackMode mode)
    {
        var fresh = new TrackPlayback(StraightTrack(mode));
        fresh.Advance(-1f);
        Assert.Equal(0.0, fresh.Elapsed);

        var playing = new TrackPlayback(StraightTrack(mode));
        playing.Advance(2f);
        playing.Advance(-1f);
        Assert.Equal(2.0, playing.Elapsed, 5);
    }

    [Theory]
    [InlineData(PlaybackMode.Once)]
    [InlineData(PlaybackMode.Loop)]
    public void ZeroDurationKeepsElapsedAtZero(PlaybackMode mode)
    {
        var points = new[] { Point(0f, 0f, 0f), Point(5f, 0f, 0f) };
        var track = new Track(points, Array.Empty<TimingKey>(), AimMode.PathTangent, mode);
        var playback = new TrackPlayback(track);

        playback.Advance(5f);
        Assert.Equal(0.0, playback.Elapsed);
    }
}
