using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Timing.TimingFixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimingCompilerTests
{
    private static void AssertTimes(float[] expected, Track track)
    {
        var actual = Times(track);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(
                MathF.Abs(expected[i] - actual[i]) <= 0.05f,
                $"key {i}: expected {expected[i]}, got {actual[i]} in [{string.Join(", ", actual)}]"
            );
    }

    // Compiler and evaluator

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
    public void AHoldPutsTheDepartureSideOnTheHoldEnd()
    {
        var track = LegEasing.Set(TrackEditing.SetHold(Build3PointTrack(), 1, 3f), 2, Easing.EaseIn);
        var keys = new TrackEvaluator(track).Keys;

        Assert.Equal(TangentMode.Flat, keys[2].OutMode);
        Assert.Equal(TangentMode.Auto, keys[1].OutMode);
    }

    [Fact]
    public void ALegBetweenCoincidentPointsTakesTheShortestLeg()
    {
        var track = TrackEditing.Append(TrackEditing.Append(TrackEditing.Empty(), Point(0f)), Point(0f));
        Assert.Equal(TrackEditing.MinLegSeconds, new TrackEvaluator(track).LegSeconds(1), 0.001f);
    }

    [Fact]
    public void ALegIsHeldAtTheLongestLeg()
    {
        var track = TrackEditing.SetLegSpeed(WithTwoPoints(TrackEditing.Empty()), 1, 0.01f);
        Assert.Equal(TrackEditing.MaxSeconds, new TrackEvaluator(track).LegSeconds(1), 0.01f);
    }

    [Fact]
    public void LegDurationClampsToTheLegRange()
    {
        Assert.Equal(5f, TimingCompiler.LegDuration(10f, 2f));
        Assert.Equal(TrackEditing.MinLegSeconds, TimingCompiler.LegDuration(0.1f, 100f));
        Assert.Equal(TrackEditing.MaxSeconds, TimingCompiler.LegDuration(10f, 0.01f));
    }

    [Fact]
    public void TheEvaluatorReadsLegsPointsAndTimes()
    {
        var evaluator = new TrackEvaluator(Build3PointTrack());
        Assert.Equal(5f, evaluator.LegSeconds(2), 0.01f);
        Assert.Equal(10f, evaluator.PointSeconds(2), 0.01f);
        Assert.Equal(10f, evaluator.LegLength(1), 1);
        Assert.Equal(1, evaluator.LegAt(2f));

        Assert.Null(new TrackEvaluator(TrackEditing.SetHold(Build3PointTrack(), 1, 3f)).LegAt(6f));
    }

    [Fact]
    public void LegAtFindsTheLegAndSkipsHolds()
    {
        var evaluator = new TrackEvaluator(TrackEditing.SetHold(Build3PointTrack(), 1, 2f)); // keys at 0, 5, 7, 12
        Assert.Equal(1, evaluator.LegAt(2f));
        Assert.Null(evaluator.LegAt(6f));
        Assert.Equal(2, evaluator.LegAt(9f));
        Assert.Null(evaluator.LegAt(13f));
    }

    [Fact]
    public void ALegHoldsBothItsEndKeysAndTheEarlierLegWinsATie()
    {
        var evaluator = new TrackEvaluator(TrackEditing.SetHold(Build3PointTrack(), 1, 2f)); // keys at 0, 5, 7, 12
        Assert.Equal(1, evaluator.LegAt(0f));
        Assert.Equal(1, evaluator.LegAt(5f));
        Assert.Equal(2, evaluator.LegAt(7f));
        Assert.Equal(2, evaluator.LegAt(12f));
    }

    [Fact]
    public void ATimingListThatDoesNotMatchThePointsIsRefused() =>
        Assert.Throws<ArgumentException>(() => new TrackEvaluator(Build3PointTrack() with { Timing = [] }));

    // Duration

    [Fact]
    public void SetDurationSolvesForTheTrackSpeed()
    {
        var track = TrackEditing.SetDuration(Build3PointTrack(), 20f);
        Assert.Equal(1f, track.Speed, 0.01f);
        AssertTimes([0f, 10f, 20f], track);
    }

    [Fact]
    public void SetDurationLeavesHoldsAndPinnedLegsAlone()
    {
        var track = TrackEditing.SetHold(TrackEditing.SetLegSpeed(Build3PointTrack(), 2, 2f), 1, 1f);
        track = TrackEditing.SetDuration(track, 16f);

        Assert.Equal(1f, track.Speed, 0.01f);
        Assert.Equal(16f, (float)new TrackEvaluator(track).Duration, 0.05f);
    }

    [Fact]
    public void SetDurationWithEveryLegPinnedChangesNothing()
    {
        var track = TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 3f), 2, 4f);
        Assert.True(TrackEditing.AllPinned(track));
        Assert.Same(track, TrackEditing.SetDuration(track, 30f));
    }

    [Fact]
    public void ATrackWithFewerThanTwoPointsIsAllPinned()
    {
        Assert.True(TrackEditing.AllPinned(TrackEditing.Empty()));
        Assert.True(TrackEditing.AllPinned(TrackEditing.Append(TrackEditing.Empty(), Point(0f))));
        Assert.False(TrackEditing.AllPinned(Build3PointTrack()));
    }

    [Fact]
    public void SetDurationStopsAtTheShortestShot()
    {
        var track = TrackEditing.SetDuration(Build3PointTrack(), 0f);
        Assert.Equal(TrackEditing.MaxSpeed, track.Speed);
        Assert.Equal(0.2f, (float)new TrackEvaluator(track).Duration, 0.01f);
    }

    [Fact]
    public void SetDurationStopsAtTheShortestShotBeforeTheFastestSpeed()
    {
        // One 10 yalm leg takes 0.1 s at MaxSpeed, under the 0.2 s shortest shot, so 0 asks for 0.2 s: 10 / 0.2 = 50 yalms per second.
        var track = TrackEditing.SetDuration(WithTwoPoints(TrackEditing.Empty()), 0f);
        Assert.Equal(50f, track.Speed, 0.01f);
        Assert.Equal(0.2f, (float)new TrackEvaluator(track).Duration, 0.001f);
    }

    [Fact]
    public void SetDurationStopsAtTheLongestLegs()
    {
        var track = TrackEditing.SetDuration(Build3PointTrack(), 99999f);
        Assert.Equal(TrackEditing.MinSpeed, track.Speed);
        Assert.Equal(1200f, (float)new TrackEvaluator(track).Duration, 0.01f);
    }

    // Legs

    [Fact]
    public void SetLegDurationPinsTheMatchingSpeed()
    {
        var track = TrackEditing.SetLegDuration(Build3PointTrack(), 1, 2f);
        Assert.Equal(5f, TrackEditing.LegSpeed(track, 1), 0.1f);
        Assert.True(TrackEditing.IsPinned(track, 1));
        AssertTimes([0f, 2f, 7f], track);
    }

    [Fact]
    public void SetLegDurationClampsToTheShortestLeg()
    {
        var track = TrackEditing.SetLegDuration(Build3PointTrack(), 1, 0f);
        Assert.Equal(TrackEditing.MaxSpeed, TrackEditing.LegSpeed(track, 1), 0.01f);
        Assert.Equal(0.1f, new TrackEvaluator(track).LegSeconds(1), 0.01f);
    }

    [Fact]
    public void SetLegSpeedClamps()
    {
        Assert.Equal(
            TrackEditing.MaxSpeed,
            TrackEditing.LegSpeed(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1000f), 1)
        );
        Assert.Equal(
            TrackEditing.MinSpeed,
            TrackEditing.LegSpeed(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 0f), 1)
        );
    }

    [Fact]
    public void ResetLegClearsThePin()
    {
        var track = TrackEditing.ResetLeg(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1f), 1);
        Assert.False(TrackEditing.IsPinned(track, 1));
        Assert.Equal(2f, TrackEditing.LegSpeed(track, 1));
        AssertTimes([0f, 5f, 10f], track);
    }

    [Fact]
    public void ResettingAnUnpinnedLegChangesNothing()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.ResetLeg(track, 1));
    }

    [Fact]
    public void SetSpeedClamps()
    {
        Assert.Equal(TrackEditing.MaxSpeed, TrackEditing.SetSpeed(Build3PointTrack(), 500f).Speed);
        Assert.Equal(TrackEditing.MinSpeed, TrackEditing.SetSpeed(Build3PointTrack(), -1f).Speed);
    }

    [Fact]
    public void SettingTheSameSpeedReturnsTheSameTrack()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.SetSpeed(track, 2f));
    }

    // Point edits

    [Fact]
    public void MovingAPointKeepsItsLegsSpeed()
    {
        var track = TrackEditing.Replace(Build3PointTrack(), 2, Point(40f));
        Assert.False(TrackEditing.IsPinned(track, 2));
        AssertTimes([0f, 5f, 20f], track);
    }

    [Fact]
    public void InsertingSplitsAPinnedLegIntoTwoPinnedHalves()
    {
        var track = TrackEditing.InsertAfter(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1f), 0, Point(5f));
        Assert.True(TrackEditing.IsPinned(track, 1));
        Assert.True(TrackEditing.IsPinned(track, 2));
        AssertTimes([0f, 5f, 10f, 15f], track);
    }

    [Fact]
    public void InsertingSplitsTheEasingAcrossTheHalves()
    {
        var track = TrackEditing.InsertAfter(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut), 0, Point(5f));
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 1));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 2));
    }

    [Fact]
    public void DeletingAMiddlePointKeepsTheFirstLegsPin()
    {
        var track = TrackEditing.Delete(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1f), 1);
        Assert.True(TrackEditing.IsPinned(track, 1));
        AssertTimes([0f, 20f], track);
    }

    [Fact]
    public void DeletingAMiddlePointJoinsTheOuterEasing()
    {
        var track = LegEasing.Set(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseIn), 2, Easing.EaseOut);
        Assert.Equal(Easing.EaseInOut, LegEasing.Read(TrackEditing.Delete(track, 1), 1));
    }

    [Fact]
    public void DeletingTheFirstPointDropsItsLeg()
    {
        var track = TrackEditing.Delete(
            TrackEditing.SetLegSpeed(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1f), 2, 2f),
            0
        );
        AssertTimes([0f, 5f], track);
        Assert.Null(track.Timing[0].LegSpeed);
    }

    [Fact]
    public void ReorderKeepsLegSpeedsInTheirSlotsAndCarriesHolds()
    {
        var track = TrackEditing.SetHold(TrackEditing.SetLegSpeed(Build3PointTrack(), 1, 1f), 2, 3f);
        track = TrackEditing.Reorder(track, [2, 0, 1]);
        var evaluator = new TrackEvaluator(track);

        Assert.Equal(1f, TrackEditing.LegSpeed(track, 1));
        Assert.True(TrackEditing.IsPinned(track, 1));
        Assert.False(TrackEditing.IsPinned(track, 2));
        Assert.Equal(3f, TrackEditing.HoldSeconds(track, 0));
        Assert.Equal(0f, TrackEditing.HoldSeconds(track, 2));

        var l1 = evaluator.LegLength(1);
        var l2 = evaluator.LegLength(2);
        AssertTimes([0f, 3f, 3f + l1, 3f + l1 + (l2 / 2f)], track);
        Assert.Equal(new[] { 0f, 0f, 1f, 2f }, evaluator.Keys.Select(k => k.Position));
    }

    // Key edits

    [Fact]
    public void SetKeyModeOnAHoldSetsOneSidePerKey()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);

        var arrival = TimingEditing.SetKeyMode(held, 1, TangentMode.Flat);
        Assert.Equal((TangentMode.Flat, TangentMode.Auto), (arrival.Timing[1].InMode, arrival.Timing[1].OutMode));

        var departure = TimingEditing.SetKeyMode(held, 2, TangentMode.Flat);
        Assert.Equal((TangentMode.Auto, TangentMode.Flat), (departure.Timing[1].InMode, departure.Timing[1].OutMode));
    }

    [Fact]
    public void SetBrokenRoundTrips()
    {
        var broken = TimingEditing.SetBroken(Build3PointTrack(), 1, true);
        Assert.True(broken.Timing[1].Broken);
        Assert.False(TimingEditing.SetBroken(broken, 1, false).Timing[1].Broken);
    }

    // The key drag

    [Fact]
    public void DraggingAHoldingPointsKeyTradesTimeWithItsHold()
    {
        var track = MoveKey(TrackEditing.SetHold(Build3PointTrack(), 1, 3f), 1, 6f);
        Assert.Equal(new TrackEvaluator(track).LegLength(1) / 6f, TrackEditing.LegSpeed(track, 1), 0.01f);
        Assert.Equal(2f, TrackEditing.HoldSeconds(track, 1), 0.01f);
        AssertTimes([0f, 6f, 8f, 13f], track);
    }

    [Fact]
    public void AHoldEndDragKeepsTheHoldAboveTheKeyGap() =>
        Assert.Equal(
            TrackEditing.MinKeyGap,
            TrackEditing.HoldSeconds(MoveKey(TrackEditing.SetHold(Build3PointTrack(), 1, 3f), 2, 0f), 1),
            0.001f
        );

    [Fact]
    public void DraggingAKeyToWhereItIsChangesNothing()
    {
        var track = Build3PointTrack();
        var evaluator = new TrackEvaluator(track);
        Assert.Same(track, TimingEditing.MoveKey(track, evaluator, 1, evaluator.Keys[1].Time));
    }

    // Easing

    [Fact]
    public void AHoldKeepsTheNextLegsEasing()
    {
        var track = TrackEditing.SetHold(LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn), 1, 2f);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
        Assert.Equal(Easing.EaseIn, LegEasing.Read(TrackEditing.SetHold(track, 1, 0f), 2));
    }
}
