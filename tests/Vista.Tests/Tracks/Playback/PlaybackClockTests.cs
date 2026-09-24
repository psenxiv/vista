using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;

namespace Vista.Tests.Tracks.Playback;

public class PlaybackClockTests
{
    private const double L = 10.0;

    [Theory]
    [InlineData(PlaybackDirection.Forward, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 10.0)]
    [InlineData(PlaybackDirection.PingPong, 20.0)]
    public void ACycleIsTheShotOrTwiceItForPingPong(PlaybackDirection direction, double expected)
        => Assert.Equal(expected, PlaybackClock.CycleLength(direction, L));

    [Theory]
    [InlineData(PlaybackDirection.Forward)]
    [InlineData(PlaybackDirection.Reverse)]
    [InlineData(PlaybackDirection.PingPong)]
    public void AZeroLengthShotHasAZeroCycleAndStaysAtZero(PlaybackDirection direction)
    {
        Assert.Equal(0.0, PlaybackClock.CycleLength(direction, 0.0));
        Assert.Equal(0.0, PlaybackClock.ShotTime(direction, 0.0, 5.0));
        Assert.Equal(0.0, PlaybackClock.ClockFor(direction, 0.0, 5.0, true));
    }

    [Theory]
    [InlineData(PlaybackDirection.Forward, 0.0, 0.0)]
    [InlineData(PlaybackDirection.Forward, 4.0, 4.0)]
    [InlineData(PlaybackDirection.Forward, 10.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 0.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 4.0, 6.0)]
    [InlineData(PlaybackDirection.Reverse, 10.0, 0.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, 4.0)]
    [InlineData(PlaybackDirection.PingPong, 10.0, 10.0)]
    [InlineData(PlaybackDirection.PingPong, 14.0, 6.0)]
    [InlineData(PlaybackDirection.PingPong, 20.0, 0.0)]
    public void ShotTimeFollowsTheDirection(PlaybackDirection direction, double clock, double expected)
        => Assert.Equal(expected, PlaybackClock.ShotTime(direction, L, clock), 9);

    [Theory]
    [InlineData(PlaybackDirection.Forward, -1.0, 0.0)]
    [InlineData(PlaybackDirection.Forward, 12.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 12.0, 0.0)]
    [InlineData(PlaybackDirection.PingPong, 25.0, 0.0)]
    public void ShotTimeClampsTheClockToTheCycle(PlaybackDirection direction, double clock, double expected)
        => Assert.Equal(expected, PlaybackClock.ShotTime(direction, L, clock), 9);

    [Theory]
    [InlineData(PlaybackDirection.Forward, 4.0, false, 4.0)]
    [InlineData(PlaybackDirection.Forward, 4.0, true, 4.0)]
    [InlineData(PlaybackDirection.Reverse, 4.0, false, 6.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, false, 4.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, true, 16.0)]
    [InlineData(PlaybackDirection.Forward, 12.0, false, 10.0)]
    [InlineData(PlaybackDirection.Reverse, -1.0, false, 10.0)]
    public void ClockForFindsTheClockGivingAShotTime(PlaybackDirection direction, double shotTime, bool onReturn, double expected)
        => Assert.Equal(expected, PlaybackClock.ClockFor(direction, L, shotTime, onReturn), 9);

    [Theory]
    [InlineData(PlaybackDirection.PingPong, 9.0, false)]
    [InlineData(PlaybackDirection.PingPong, 10.0, false)]
    [InlineData(PlaybackDirection.PingPong, 10.5, true)]
    [InlineData(PlaybackDirection.PingPong, 20.0, true)]
    [InlineData(PlaybackDirection.Reverse, 15.0, false)]
    [InlineData(PlaybackDirection.Forward, 15.0, false)]
    public void OnlyPingPongPastTheShotIsOnItsReturnPass(PlaybackDirection direction, double clock, bool expected)
        => Assert.Equal(expected, PlaybackClock.OnReturnPass(direction, L, clock));

    [Theory]
    [InlineData(PlaybackDirection.Forward)]
    [InlineData(PlaybackDirection.Reverse)]
    [InlineData(PlaybackDirection.PingPong)]
    public void ClockForUndoesShotTimeAcrossTheCycle(PlaybackDirection direction)
    {
        var cycle = PlaybackClock.CycleLength(direction, L);
        for (var clock = 0.0; clock <= cycle; clock += 0.5)
        {
            var shot = PlaybackClock.ShotTime(direction, L, clock);
            var onReturn = PlaybackClock.OnReturnPass(direction, L, clock);
            Assert.Equal(clock, PlaybackClock.ClockFor(direction, L, shot, onReturn), 9);
        }
    }
}
