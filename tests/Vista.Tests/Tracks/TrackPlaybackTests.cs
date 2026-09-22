using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class TrackPlaybackTests
{
    private static ControlPoint Point(float x, float y, float z)
        => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static Track StraightTrack(bool loop)
    {
        var track = TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x, 0f, 0f));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    [Fact]
    public void PositionAtFiveSecondsMatchesUnder60FpsAnd30FpsDeltaSequences()
    {
        var sixty = new TrackPlayback(StraightTrack(false));
        var thirty = new TrackPlayback(StraightTrack(false));

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
        var playback = new TrackPlayback(StraightTrack(false));

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
        var playback = new TrackPlayback(StraightTrack(true));

        var wrapped = playback.Advance(10f); // exactly the duration: wraps to elapsed 0
        Assert.False(playback.IsFinished);
        Assert.Equal(0.0, playback.Elapsed, 5);

        var evaluator = new TrackEvaluator(StraightTrack(true));
        var firstFrame = evaluator.Evaluate(0.0);
        Assert.Equal(firstFrame, wrapped);
    }

    [Fact]
    public void ALoopTrackNeverFinishesPastItsDuration()
    {
        var playback = new TrackPlayback(StraightTrack(true));

        playback.Advance(12f);
        Assert.False(playback.IsFinished);
        Assert.Equal(2.0, playback.Elapsed, 3);
    }

    [Fact]
    public void RestartResetsElapsedAndClearsIsFinished()
    {
        var playback = new TrackPlayback(StraightTrack(false));
        playback.Advance(20f);
        Assert.True(playback.IsFinished);

        playback.Restart();
        Assert.Equal(0.0, playback.Elapsed);
        Assert.False(playback.IsFinished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeFrameTimeDoesNotRunTimeBackwards(bool loop)
    {
        var fresh = new TrackPlayback(StraightTrack(loop));
        fresh.Advance(-1f);
        Assert.Equal(0.0, fresh.Elapsed);

        var playing = new TrackPlayback(StraightTrack(loop));
        playing.Advance(2f);
        playing.Advance(-1f);
        Assert.Equal(2.0, playing.Elapsed, 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroDurationKeepsElapsedAtZero(bool loop)
    {
        var track = TrackEditing.Append(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), Point(0f, 0f, 0f));
        var playback = new TrackPlayback(track);

        playback.Advance(5f);
        Assert.Equal(0.0, playback.Elapsed);
    }

    [Fact]
    public void SeekOnceClampsAndFinishesAtTheEnd()
    {
        var playback = new TrackPlayback(StraightTrack(false));
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.Elapsed);
        Assert.False(playback.IsFinished);

        playback.Seek(99.0);
        Assert.Equal(10.0, playback.Elapsed, 5);
        Assert.True(playback.IsFinished);

        playback.Seek(-3.0);
        Assert.Equal(0.0, playback.Elapsed);
    }

    [Fact]
    public void SeekingAFinishedShotBackUnfinishesIt()
    {
        var playback = new TrackPlayback(StraightTrack(false));
        playback.Advance(20f);
        Assert.True(playback.IsFinished);

        playback.Seek(3.0);
        Assert.False(playback.IsFinished);
        playback.Advance(1f);
        Assert.Equal(4.0, playback.Elapsed, 5);
    }

    [Fact]
    public void SeekLoopWraps()
    {
        var playback = new TrackPlayback(StraightTrack(true));
        playback.Seek(23.0);
        Assert.Equal(3.0, playback.Elapsed, 5);

        playback.Seek(-1.0);
        Assert.Equal(9.0, playback.Elapsed, 5);
    }
}
