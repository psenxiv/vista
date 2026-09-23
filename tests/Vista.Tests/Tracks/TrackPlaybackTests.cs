using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class TrackPlaybackTests
{
    [Theory]
    [InlineData(PlaybackDirection.Forward)]
    [InlineData(PlaybackDirection.Reverse)]
    [InlineData(PlaybackDirection.PingPong)]
    public void PositionAtFiveSecondsMatchesUnder60FpsAnd30FpsDeltaSequences(PlaybackDirection direction)
    {
        var sixty = new TrackPlayback(StraightTrack(false, direction));
        var thirty = new TrackPlayback(StraightTrack(false, direction));

        CameraState? last60 = null;
        for (var i = 0; i < 300; i++)
            last60 = sixty.Advance(1f / 60f);

        CameraState? last30 = null;
        for (var i = 0; i < 150; i++)
            last30 = thirty.Advance(1f / 30f);

        // A 10 s shot at clock 5 s is 5 s in whichever direction it plays: forward 5, reverse 10 - 5, ping-pong 5 on the outward pass.
        Assert.Equal(5.0, sixty.ShotTime, 3);
        Assert.Equal(5.0, thirty.ShotTime, 3);
        Assert.NotNull(last60);
        Assert.NotNull(last30);
        Assert.Equal(last60!.Value.Position.X, last30!.Value.Position.X, 3);
        Assert.Equal(last60.Value.Position.Y, last30.Value.Position.Y, 3);
        Assert.Equal(last60.Value.Position.Z, last30.Value.Position.Z, 3);
        Assert.Equal(last60.Value.Fov, last30.Value.Fov, 3);
    }

    [Fact]
    public void AFinishedForwardTrackHoldsItsLastFrame()
    {
        var playback = new TrackPlayback(StraightTrack(false));

        var atEnd = playback.Advance(15f);
        Assert.True(playback.IsFinished);
        Assert.Equal(10.0, playback.ShotTime);

        var afterEnd = playback.Advance(5f);
        Assert.True(playback.IsFinished);
        Assert.Equal(10.0, playback.ShotTime);
        Assert.Equal(atEnd, afterEnd);
    }

    [Fact]
    public void ALoopTrackCutsBackToItsFirstFrameAfterItsLastKey()
    {
        var playback = new TrackPlayback(StraightTrack(true));

        var wrapped = playback.Advance(10f); // exactly the duration: wraps to shot time 0
        Assert.False(playback.IsFinished);
        Assert.Equal(0.0, playback.ShotTime, 5);

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
        Assert.Equal(2.0, playback.ShotTime, 3);
    }

    [Fact]
    public void RestartResetsShotTimeAndClearsIsFinished()
    {
        var playback = new TrackPlayback(StraightTrack(false));
        playback.Advance(20f);
        Assert.True(playback.IsFinished);

        playback.Restart();
        Assert.Equal(0.0, playback.ShotTime);
        Assert.False(playback.IsFinished);
    }

    [Theory]
    // A 10 s shot. At clock 0 the shot time is 0 forward, 10 reversed; at clock 2 it is 2 forward, 8 reversed.
    [InlineData(false, PlaybackDirection.Forward, 0.0, 2.0)]
    [InlineData(true, PlaybackDirection.Forward, 0.0, 2.0)]
    [InlineData(false, PlaybackDirection.Reverse, 10.0, 8.0)]
    [InlineData(true, PlaybackDirection.Reverse, 10.0, 8.0)]
    [InlineData(false, PlaybackDirection.PingPong, 0.0, 2.0)]
    [InlineData(true, PlaybackDirection.PingPong, 0.0, 2.0)]
    public void NegativeFrameTimeDoesNotRunTimeBackwards(bool loop, PlaybackDirection direction, double atZero, double atTwo)
    {
        var fresh = new TrackPlayback(StraightTrack(loop, direction));
        fresh.Advance(-1f);
        Assert.Equal(atZero, fresh.ShotTime, 5);

        var playing = new TrackPlayback(StraightTrack(loop, direction));
        playing.Advance(2f);
        playing.Advance(-1f);
        Assert.Equal(atTwo, playing.ShotTime, 5);
    }

    [Theory]
    [InlineData(false, PlaybackDirection.Forward)]
    [InlineData(true, PlaybackDirection.Forward)]
    [InlineData(false, PlaybackDirection.Reverse)]
    [InlineData(true, PlaybackDirection.Reverse)]
    [InlineData(false, PlaybackDirection.PingPong)]
    [InlineData(true, PlaybackDirection.PingPong)]
    public void ZeroDurationKeepsShotTimeAtZero(bool loop, PlaybackDirection direction)
    {
        var track = TrackEditing.Append(TrackEditing.SetDirection(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), direction), Point(0f, 0f, 0f));
        var playback = new TrackPlayback(track);

        playback.Advance(5f);
        Assert.Equal(0.0, playback.ShotTime);
        Assert.Equal(!loop, playback.IsFinished);
    }

    [Fact]
    public void SeekForwardClampsAndFinishesAtTheEnd()
    {
        var playback = new TrackPlayback(StraightTrack(false));
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.ShotTime);
        Assert.False(playback.IsFinished);

        playback.Seek(99.0);
        Assert.Equal(10.0, playback.ShotTime, 5);
        Assert.True(playback.IsFinished);

        playback.Seek(-3.0);
        Assert.Equal(0.0, playback.ShotTime);
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
        Assert.Equal(4.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekingALoopToItsEndShowsTheFirstFrame()
    {
        var playback = new TrackPlayback(StraightTrack(true));
        playback.Seek(10.0);
        Assert.Equal(0.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);

        playback.Seek(23.0);
        Assert.Equal(0.0, playback.ShotTime, 5);

        playback.Seek(-1.0);
        Assert.Equal(0.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekingALoopingReverseShotToItsStartShowsItsEnd()
    {
        var playback = new TrackPlayback(StraightTrack(true, PlaybackDirection.Reverse));
        playback.Seek(0.0);
        Assert.Equal(10.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ReverseStartsAtTheEndAndWalksBack()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        Assert.Equal(10.0, playback.ShotTime, 5);

        playback.Advance(3f);
        Assert.Equal(7.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);
    }

    [Theory]
    [InlineData(PlaybackDirection.Reverse, 15f)]
    [InlineData(PlaybackDirection.PingPong, 25f)]
    public void ReverseAndPingPongFinishHoldingTheFirstFrame(PlaybackDirection direction, float past)
    {
        var track = StraightTrack(false, direction);
        var playback = new TrackPlayback(track);

        var atEnd = playback.Advance(past);
        Assert.True(playback.IsFinished);
        Assert.Equal(0.0, playback.ShotTime, 5);
        Assert.Equal(new TrackEvaluator(track).Evaluate(0.0), atEnd);
        Assert.Equal(atEnd, playback.Advance(5f));
    }

    [Fact]
    public void ReverseLoopCutsBackToTheEnd()
    {
        var playback = new TrackPlayback(StraightTrack(true, PlaybackDirection.Reverse));
        playback.Advance(12f);
        Assert.Equal(8.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void PingPongGoesOutAndComesBack()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(4f);
        Assert.Equal(4.0, playback.ShotTime, 5);

        playback.Advance(10f);
        Assert.Equal(6.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void PingPongFinishesAfterTwiceTheShot()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(19.5f);
        Assert.False(playback.IsFinished);
        Assert.Equal(0.5, playback.ShotTime, 3);

        playback.Advance(1f);
        Assert.True(playback.IsFinished);
        Assert.Equal(0.0, playback.ShotTime, 5);
    }

    [Fact]
    public void PingPongLoopTurnsBackAtTheStartWithoutACut()
    {
        var playback = new TrackPlayback(StraightTrack(true, PlaybackDirection.PingPong));
        playback.Advance(19f);
        Assert.Equal(1.0, playback.ShotTime, 3);

        playback.Advance(2f);
        Assert.Equal(1.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void APingPongTurnaroundStaysForTwiceTheHold()
    {
        // The last point holds 2 s, so the shot is 12 s and the camera sits at x = 10 from clock 10 to 14.
        var track = TrackEditing.SetHold(StraightTrack(false, PlaybackDirection.PingPong), 2, 2f);

        float XAt(float clock)
        {
            var playback = new TrackPlayback(track);
            return playback.Advance(clock)!.Value.Position.X;
        }

        Assert.Equal(10f, XAt(10.5f), 3);
        Assert.Equal(10f, XAt(12f), 3);
        Assert.Equal(10f, XAt(13.5f), 3);
        Assert.True(XAt(9.5f) < 9.99f);
        Assert.True(XAt(14.5f) < 9.99f);
    }

    [Fact]
    public void SeekInReverseSetsTheShotTimeAndFinishesAtTheStart()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);

        playback.Advance(1f);
        Assert.Equal(3.0, playback.ShotTime, 5);

        playback.Seek(0.0);
        Assert.True(playback.IsFinished);
    }

    [Fact]
    public void SeekInPingPongKeepsTheOutwardPass()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(2f);
        playback.Seek(6.0);
        playback.Advance(1f);
        Assert.Equal(7.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekInPingPongKeepsTheReturnPass()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(13f);
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.ShotTime, 5);

        playback.Advance(1f);
        Assert.Equal(3.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekingAFinishedPingPongBackCarriesOnHome()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(25f);
        Assert.True(playback.IsFinished);

        playback.Seek(3.0);
        Assert.False(playback.IsFinished);
        playback.Advance(1f);
        Assert.Equal(2.0, playback.ShotTime, 5);
    }

    [Fact]
    public void RestartPutsAReverseShotBackAtItsEnd()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        playback.Advance(20f);
        playback.Restart();
        Assert.Equal(10.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);
    }


    private static void GuardAt(NearbyCharacters characters, float x)
        => characters.Update([new LoadedCharacter("Guard", null, new Vector3(x, -1.3f, -10f))]);

    [Fact]
    public void AWatchedCharacterIsAimedAtAndASeekOrRestartSnapsBackOntoThem()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.WatchTarget), Point(0f, 0f, 0f)) with { TargetName = "Guard", Smoothing = 1f };
        var playback = new TrackPlayback(track, characters);

        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(1f / 60f)!.Value, 3);

        GuardAt(characters, 10f);
        playback.Advance(0.5f);

        playback.Seek(0.0);
        AimsAt(new Vector3(10f, 0f, -10f), playback.Advance(1f / 60f)!.Value, 3);

        GuardAt(characters, -10f);
        playback.Restart();
        AimsAt(new Vector3(-10f, 0f, -10f), playback.Advance(1f / 60f)!.Value, 3);
    }
}
