using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Timing.TimingFixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimingCompilerTests
{
    [Fact]
    public void AStraightTrackCompilesAKeyPerPointAtTheTrackSpeed()
    {
        var track = Build3PointTrack();
        var keys = new TrackEvaluator(track).Keys;

        Assert.Equal(2f, track.Speed);
        AssertTimes([0f, 5f, 10f], track);
        Assert.Equal(new[] { 0f, 1f, 2f }, keys.Select(k => k.Position));
    }

    [Fact]
    public void TheTrackSpeedSetsEveryUnpinnedLeg() =>
        AssertTimes([0f, 2f, 4f], TrackEditing.SetSpeed(Build3PointTrack(), 5f));

    [Fact]
    public void APinnedLegKeepsItsOwnSpeed()
    {
        var track = TrackEditing.SetLegSpeed(Build3PointTrack(), 2, 1f);

        AssertTimes([0f, 5f, 15f], track);
        Assert.True(TrackEditing.IsPinned(track, 2));
        Assert.False(TrackEditing.IsPinned(track, 1));
    }

    [Fact]
    public void AHoldCompilesAHoldEnd()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 3f);
        var keys = new TrackEvaluator(track).Keys;

        AssertTimes([0f, 5f, 8f, 13f], track);
        Assert.Equal(new[] { 0f, 1f, 1f, 2f }, keys.Select(k => k.Position));
        Assert.Equal(
            new[] { KeyRole.Point, KeyRole.Point, KeyRole.HoldEnd, KeyRole.Point },
            Enumerable.Range(0, 4).Select(k => TrackEditing.RoleOf(track, k))
        );
        Assert.Equal(4, TrackEditing.KeyCount(track));
    }

    [Fact]
    public void LegDurationClampsToTheLegRange()
    {
        Assert.Equal(5f, TimingCompiler.LegDuration(10f, 2f));
        Assert.Equal(TrackEditing.MinLegSeconds, TimingCompiler.LegDuration(0.1f, 100f));
        Assert.Equal(TrackEditing.MaxSeconds, TimingCompiler.LegDuration(10f, 0.01f));
    }

    [Fact]
    public void ATimingListThatDoesNotMatchThePointsIsRefused() =>
        Assert.Throws<ArgumentException>(() => new TrackEvaluator(Build3PointTrack() with { Timing = [] }));
}
