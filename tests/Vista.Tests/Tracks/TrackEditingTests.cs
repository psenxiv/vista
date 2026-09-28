using System.Numerics;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Tracks.Timing.TimingFixtures;

namespace Vista.Tests.Tracks;

public class TrackEditingTests
{
    private static IReadOnlyList<TimingKey> Keys(Track track) => new TrackEvaluator(track).Keys;

    private static float LegSeconds(Track track, int leg) => new TrackEvaluator(track).LegSeconds(leg);

    private static float Total(Track track) => (float)new TrackEvaluator(track).Duration;

    [Fact]
    public void PointLegAndKeyIndicesAreCheckedAtBothEnds()
    {
        // Three points: points 0 to 2, legs 1 and 2, and with a hold on point 1, keys 0 to 3.
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);

        Assert.Equal(
            [false, true, true, true, false],
            new[] { -1, 0, 1, 2, 3 }.Select(i => TrackEditing.IsPoint(track, i))
        );
        Assert.Equal([false, true, true, false], new[] { 0, 1, 2, 3 }.Select(i => TrackEditing.IsLeg(track, i)));
        Assert.Equal([false, true, true, false], new[] { -1, 0, 3, 4 }.Select(i => TrackEditing.IsKey(track, i)));
    }

    [Fact]
    public void ANewTrackMovesAtFiveYalmsPerSecond() => Assert.Equal(5f, TrackEditing.Empty().Speed);

    [Fact]
    public void EmptyHasNoPointsNoKeysAndPlaysForwardWithoutLooping()
    {
        var track = TrackEditing.Empty();

        Assert.Empty(track.Points);
        Assert.Empty(track.Timing);
        Assert.Empty(Keys(track));
        Assert.Equal(TrackEditing.DefaultSpeed, track.Speed);
        Assert.Equal(PlaybackDirection.Forward, track.Direction);
        Assert.False(track.Loop);
        Assert.Equal(AimMode.AimKeys, track.Aim);
    }

    [Fact]
    public void EmptyAcceptsAnExplicitAimMode()
    {
        var track = TrackEditing.Empty(AimMode.PathTangent);
        Assert.Equal(AimMode.PathTangent, track.Aim);
    }

    [Fact]
    public void EmptyGivesEachTrackANewIdAndTheNameAsked()
    {
        var first = TrackEditing.Empty();
        var second = TrackEditing.Empty(name: "Crane");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Track 1", first.Name);
        Assert.Equal("Crane", second.Name);
    }

    [Fact]
    public void ClearEmptiesTheTrackButKeepsItsIdAndName()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent, "Crane"), Point(1f, 2f, 3f));
        var cleared = TrackEditing.Clear(track);

        Assert.Equal(track.Id, cleared.Id);
        Assert.Equal("Crane", cleared.Name);
        Assert.Empty(cleared.Points);
        Assert.Empty(cleared.Timing);
        Assert.Equal(AimMode.AimKeys, cleared.Aim);
    }

    [Fact]
    public void ClearKeepsTheAnchor()
    {
        var anchor = new Anchor(new Vector3(1f, 2f, 3f), 0.5f);
        var track = TrackEditing.Append(
            TrackEditing.Empty() with
            {
                Anchor = anchor,
                AnchorPlaced = true,
            },
            Point(1f, 2f, 3f)
        );
        var cleared = TrackEditing.Clear(track);

        Assert.Equal(anchor, cleared.Anchor);
        Assert.True(cleared.AnchorPlaced);
    }

    [Fact]
    public void AppendFirstPointGetsKeyAtTimeZero()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(1f, 2f, 3f));
        var keys = Keys(track);

        Assert.Single(track.Points);
        Assert.Single(track.Timing);
        Assert.Single(keys);
        Assert.Equal(0f, keys[0].Time);
        Assert.Equal(0f, keys[0].Position);
        Assert.Equal(TangentMode.Auto, keys[0].InMode);
    }

    [Fact]
    public void AppendLaterPointFollowsTheTrackSpeed()
    {
        var track = TrackEditing.Empty() with { Speed = 2f };
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        var keys = Keys(track);

        Assert.Equal(2, keys.Count);
        Assert.Equal(5f, keys[1].Time, 3);
        Assert.Equal(1f, keys[1].Position);
        Assert.False(TrackEditing.IsPinned(track, 1));
    }

    [Fact]
    public void AppendAfterAHoldPlacesTheNewKeyOneLegAfterTheHoldKey()
    {
        var track = Build3PointTrack(); // keys at 0, 5, 10
        track = TrackEditing.SetHold(track, 2, 4f); // point 2's second key lands at 14

        track = TrackEditing.Append(track, Point(30f, 0f, 0f));
        var keys = Keys(track);

        Assert.Equal(4, track.Points.Count);
        Assert.Equal(5, keys.Count);
        Assert.Equal(3f, keys[^1].Position);
        Assert.Equal(19f, keys[^1].Time, 3);
    }

    [Fact]
    public void AppendingAControlPointDoesNotRetimeTheExistingOnes()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        track = TrackEditing.SetHold(track, 1, 2f);

        var keysBefore = Keys(track).ToArray();
        var evaluatorBefore = new TrackEvaluator(track);
        var positionsBefore = keysBefore.Select(k => evaluatorBefore.Evaluate(k.Time)!.Value.Position).ToArray();

        track = TrackEditing.Append(track, Point(20f, 0f, 0f));

        Assert.Equal(keysBefore, Keys(track).Take(keysBefore.Length));

        var evaluatorAfter = new TrackEvaluator(track);
        for (var i = 0; i < keysBefore.Length; i++)
            Assert.Equal(positionsBefore[i], evaluatorAfter.Evaluate(keysBefore[i].Time)!.Value.Position);
    }

    [Fact]
    public void SetLegDurationShiftsLaterKeysByTheDifference()
    {
        var track = Build3PointTrack(); // times 0, 5, 10
        track = TrackEditing.SetLegDuration(track, 1, 8f);
        var keys = Keys(track);

        Assert.Equal(8f, keys[1].Time, 3);
        Assert.Equal(13f, keys[2].Time, 3);
        Assert.Equal(5f, LegSeconds(track, 2), 3);
        // Leg 1 is now 10 yalms in 8 s, pinned to that speed.
        Assert.Equal(1.25f, TrackEditing.LegSpeed(track, 1), 0.01f);
        Assert.True(TrackEditing.IsPinned(track, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SetLegDurationRejectsAnOutOfRangeIndex(int leg)
    {
        var track = Build3PointTrack(); // 3 points; valid legs are 1..2
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLegDuration(track, leg, 5f));
    }

    [Fact]
    public void RejectionMessagesAreOnlyThePlainMessage()
    {
        var track = Build3PointTrack();
        Assert.Equal(
            "Leg index must be 1..2 for a 3-point track.",
            Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLegDuration(track, 3, 1f)).Message
        );
        Assert.Equal(
            "Hold index must be 0..2 for a 3-point track.",
            Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 3, 1f)).Message
        );
    }

    [Fact]
    public void EachIndexRefusalNamesWhatItIndexes()
    {
        // Three points are indices 0..2, and with no holds they are the three keys, 0..2.
        var track = Build3PointTrack();

        string Refusal(Action act) => Assert.Throws<ArgumentOutOfRangeException>(act).Message;

        Assert.Equal("Point index must be 0..2 for a 3-point track.", Refusal(() => TrackEditing.PointKey(track, 3)));
        Assert.Equal("Hold index must be 0..2 for a 3-point track.", Refusal(() => TrackEditing.HoldSeconds(track, 3)));
        Assert.Equal(
            "Insert index must be 0..2 for a 3-point track.",
            Refusal(() => TrackEditing.InsertAfter(track, 3, Point(0f)))
        );
        Assert.Equal("Delete index must be 0..2 for a 3-point track.", Refusal(() => TrackEditing.Delete(track, 3)));
        Assert.Equal(
            "Replace index must be 0..2 for a 3-point track.",
            Refusal(() => TrackEditing.Replace(track, 3, Point(0f)))
        );
        Assert.Equal("Key index must be 0..2.", Refusal(() => TrackEditing.RoleOf(track, 3)));
    }

    [Fact]
    public void SetLegDurationClampsNonPositiveSecondsToTheShortestLeg()
    {
        var track = Build3PointTrack();
        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(TrackEditing.SetLegDuration(track, 1, 0f), 1), 3);
        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(TrackEditing.SetLegDuration(track, 1, -1f), 1), 3);
    }

    [Fact]
    public void SetLegDurationClampsALongLegToTheLongest()
    {
        // 9999 s is past MaxSeconds, 600 s.
        var track = Build3PointTrack();
        Assert.Equal(600f, LegSeconds(TrackEditing.SetLegDuration(track, 1, 9999f), 1), 1);
    }

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

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ATimingValueThatIsNotFiniteChangesNothing(float value)
    {
        var track = Build3PointTrack();

        Assert.Same(track, TrackEditing.SetSpeed(track, value));
        Assert.Same(track, TrackEditing.SetDuration(track, value));
        Assert.Same(track, TrackEditing.SetLegSpeed(track, 1, value));
        Assert.Same(track, TrackEditing.SetLegDuration(track, 1, value));
        Assert.Same(track, TrackEditing.SetHold(track, 1, value));
    }

    // Leg and track speed

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

    [Fact]
    public void HoldSecondsIsZeroWhenThereIsNoSecondKey()
    {
        var track = Build3PointTrack();
        Assert.Equal(0f, TrackEditing.HoldSeconds(track, 1));
    }

    [Fact]
    public void SetHoldAddsASecondKeyAndShiftsLaterKeysByTheHoldLength()
    {
        var track = Build3PointTrack(); // times 0, 5, 10
        track = TrackEditing.SetHold(track, 1, 3f);

        Assert.Equal(3f, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(5f, LegSeconds(track, 2), 3);
    }

    [Fact]
    public void SetHoldResizesAnExistingHold()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        track = TrackEditing.SetHold(track, 1, 7f);

        Assert.Equal(4, Keys(track).Count);
        Assert.Equal(7f, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(5f, LegSeconds(track, 2), 3);
    }

    [Fact]
    public void SetHoldZeroRemovesTheSecondKey()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        track = TrackEditing.SetHold(track, 1, 0f);

        Assert.Equal(3, Keys(track).Count);
        Assert.Equal(0f, TrackEditing.HoldSeconds(track, 1));
        Assert.Equal(5f, LegSeconds(track, 2), 3);
    }

    [Fact]
    public void SetHoldClampsNegativeSecondsToNoHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.HoldSeconds(TrackEditing.SetHold(track, 1, -1f), 1));
    }

    [Fact]
    public void SetHoldClampsALongHoldToTheLongest()
    {
        // 9999 s is past MaxSeconds, 600 s.
        var track = Build3PointTrack();
        Assert.Equal(600f, TrackEditing.HoldSeconds(TrackEditing.SetHold(track, 1, 9999f), 1));
    }

    [Fact]
    public void SetHoldRoundsATinyHoldDownToZero()
    {
        var track = Build3PointTrack();
        Assert.Equal(0f, TrackEditing.HoldSeconds(TrackEditing.SetHold(track, 1, 0.0001f), 1));
    }

    [Fact]
    public void SetHoldRejectsAnOutOfRangeIndex()
    {
        var track = Build3PointTrack(); // valid holds are 0..2
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 3, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, -1, 1f));
    }

    [Fact]
    public void LegSecondsAndHoldSecondsAlsoValidateTheIndex()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => LegSeconds(track, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(track, 3));
    }

    [Fact]
    public void LegOnATrackWithFewerThanTwoPointsSaysItHasNoLegs()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f, 0f, 0f));
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.LegSpeed(track, 1));
        Assert.Equal("This track has no legs.", ex.Message);
    }

    [Fact]
    public void HoldOnAnEmptyTrackSaysItHasNoPoints()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(TrackEditing.Empty(), 0));
        Assert.Equal("This track has no points.", ex.Message);
    }

    [Fact]
    public void SettingALegAfterAHoldKeepsTheHold()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        var holdBefore = TrackEditing.HoldSeconds(track, 1);

        track = TrackEditing.SetLegDuration(track, 2, 8f);

        Assert.Equal(holdBefore, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(8f, LegSeconds(track, 2), 3);
    }

    [Fact]
    public void GeneratedKeysUseAutoTangentsWithZeroTangents()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 2f);

        foreach (var key in Keys(track))
        {
            Assert.Equal(TangentMode.Auto, key.InMode);
            Assert.Equal(0f, key.InTangent);
            Assert.Equal(0f, key.OutTangent);
        }
    }

    [Fact]
    public void SetDirectionChangesItWithoutTouchingPointsOrTiming()
    {
        var track = Build3PointTrack();
        var updated = TrackEditing.SetDirection(track, PlaybackDirection.PingPong);

        Assert.Equal(PlaybackDirection.PingPong, updated.Direction);
        Assert.Equal(track.Points, updated.Points);
        Assert.Equal(track.Timing, updated.Timing);
        Assert.Equal(track.Speed, updated.Speed);
        Assert.Same(updated, TrackEditing.SetDirection(updated, PlaybackDirection.PingPong));
    }

    [Fact]
    public void SetLoopChangesItWithoutTouchingPointsOrTiming()
    {
        var track = Build3PointTrack();
        var updated = TrackEditing.SetLoop(track, true);

        Assert.True(updated.Loop);
        Assert.Equal(track.Points, updated.Points);
        Assert.Equal(track.Timing, updated.Timing);
        Assert.Equal(track.Speed, updated.Speed);
        Assert.Same(updated, TrackEditing.SetLoop(updated, true));
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
    public void InsertAfterTimesEachHalfByItsLength()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 0, Point(2f, 0f, 0f));
        var evaluator = new TrackEvaluator(result);

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(evaluator.LegLength(1) / 2f, evaluator.LegSeconds(1), 3);
        Assert.Equal(evaluator.LegLength(2) / 2f, evaluator.LegSeconds(2), 3);
        Assert.Equal(5f, evaluator.LegSeconds(3), 3);
    }

    [Fact]
    public void InsertAfterKeepsTheSelectedPointsHoldAndGivesTheNewPointNone()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 2f);
        var result = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));

        Assert.Equal(2f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 1));
        Assert.Equal(Total(track), Total(result), 1);
    }

    [Fact]
    public void InsertAfterTheLastPointAppends()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 2, Point(30f, 0f, 0f));

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(5f, LegSeconds(result, 3), 3);
        Assert.False(TrackEditing.IsPinned(result, 3));
    }

    [Fact]
    public void InsertBetweenCoincidentPointsSplitsEvenly()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        var result = TrackEditing.InsertAfter(track, 0, Point(0f, 0f, 0f));

        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(result, 1), 4);
        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(result, 2), 4);
    }

    [Fact]
    public void DuplicatingAPointCopiesItAndItsTimingStraightAfterIt()
    {
        var track = Build3PointTrack();
        track = TrackEditing.Replace(track, 1, Point(10f, yaw: 0.5f, pitch: 0.2f, fov: 1.2f, roll: 0.1f));
        track = TrackEditing.SetLegSpeed(track, 1, 4f);
        track = TrackEditing.SetHold(track, 1, 2f);

        var result = TrackEditing.Duplicate(track, 1);
        var evaluator = new TrackEvaluator(result);

        Assert.Equal([0f, 10f, 10f, 20f], result.Points.Select(p => p.Position.X));
        Assert.Equal(track.Points[1], result.Points[2]);
        Assert.Equal(track.Timing[1], result.Timing[2]);
        Assert.Equal(track.Timing[2], result.Timing[3]);
        // Leg 1 is 10 yalms at its pinned 4: 2.5 s, then point 1 holds 2 s to 4.5 s. The copy's leg is 0 yalms,
        // timed as 0.1 at its copied 4 (0.025 s), so it takes the shortest leg, 0.1 s: 4.6 s. It holds 2 s to 6.6 s,
        // then 10 yalms at the track's 2 is 5 s: 11.6 s.
        Assert.Equal(2.5f, evaluator.PointSeconds(1), 1e-3f);
        Assert.Equal(4.6f, evaluator.PointSeconds(2), 1e-3f);
        Assert.Equal(11.6f, evaluator.PointSeconds(3), 1e-3f);
        Assert.Equal(11.6, evaluator.Duration, 1e-3);
    }

    [Fact]
    public void DuplicatingTheLastPointAddsAShortLegAtTheTrackSpeed()
    {
        var result = TrackEditing.Duplicate(Build3PointTrack(), 2);

        // The copy's leg is 0 yalms, timed as 0.1 at 2 yalms a second (0.05 s), so it takes the shortest leg, 0.1 s.
        Assert.Equal(4, result.Points.Count);
        Assert.Equal(result.Points[2], result.Points[3]);
        Assert.Equal(10.1, new TrackEvaluator(result).Duration, 1e-3);
    }

    [Fact]
    public void DuplicatingRefusesAPointOutOfRangeAndAFollowTargetTrack()
    {
        var follow = TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), Point(0f));

        Assert.Equal(
            "Duplicate index must be 0..2 for a 3-point track.",
            Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Duplicate(Build3PointTrack(), 3)).Message
        );
        Assert.Equal(
            TrackEditing.FollowHasOnePoint,
            Assert.Throws<ArgumentException>(() => TrackEditing.Duplicate(follow, 0)).Message
        );
        Assert.False(TrackEditing.CanDuplicate(follow));
        Assert.True(TrackEditing.CanDuplicate(Build3PointTrack()));
    }

    [Fact]
    public void DeletingAMiddlePointMergesItsLegsAndDropsItsHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var result = TrackEditing.Delete(track, 1);
        var evaluator = new TrackEvaluator(result);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(evaluator.LegLength(1) / 2f, evaluator.LegSeconds(1), 3);
        Assert.Equal(10f, Total(result), 1);
    }

    [Fact]
    public void DeletingTheFirstPointDropsItsHoldAndLeg()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 1f);
        var result = TrackEditing.Delete(track, 0);
        var keys = Keys(result);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(0f, keys[0].Time);
        Assert.Equal(0f, keys[0].Position);
        Assert.Equal(5f, LegSeconds(result, 1), 3);
        Assert.Equal(5f, Total(result), 3);
    }

    [Fact]
    public void DeletingTheLastPointDropsItsLegAndHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 2, 2f);
        var result = TrackEditing.Delete(track, 2);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(5f, Total(result), 3);
    }

    [Fact]
    public void DeletingTheOnlyPointEmptiesTheTrackButKeepsItsModesAndSpeed()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent), Point(0f, 0f, 0f));
        track = TrackEditing.SetSpeed(
            TrackEditing.SetLoop(TrackEditing.SetDirection(track, PlaybackDirection.Reverse), true),
            7f
        );
        var result = TrackEditing.Delete(track, 0);

        Assert.Empty(result.Points);
        Assert.Empty(result.Timing);
        Assert.Equal(AimMode.PathTangent, result.Aim);
        Assert.Equal(PlaybackDirection.Reverse, result.Direction);
        Assert.True(result.Loop);
        Assert.Equal(7f, result.Speed);
    }

    [Fact]
    public void MovingAPointCarriesItsHoldAndLeavesLegSpeedsInTheirSlots()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetLegSpeed(track, 1, 3f);
        track = TrackEditing.SetLegSpeed(track, 2, 7f);
        track = TrackEditing.SetHold(track, 2, 1f);
        var moved = track.Points[2];

        var result = TrackEditing.Reorder(track, [2, 0, 1]);

        Assert.Same(moved, result.Points[0]);
        Assert.Same(track.Points[0], result.Points[1]);
        Assert.Same(track.Points[1], result.Points[2]);
        Assert.Equal(1f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 2));
        Assert.Equal(3f, TrackEditing.LegSpeed(result, 1));
        Assert.Equal(7f, TrackEditing.LegSpeed(result, 2));

        // Point 0's key sits at time 0, then its 1 s hold departs at time 1. Leg 1 (point 0 to point
        // 1) takes its length over its pinned 3 yalms/s, leg 2 (point 1 to point 2) its length over
        // its pinned 7 yalms/s; each length is the reordered points' spline distance, not derivable
        // by hand since the points no longer run in a straight line. Key positions are point indices,
        // unaffected by the reorder's lengths or speeds: 0 twice (arrival and hold departure), then 1, 2.
        var evaluator = new TrackEvaluator(result);
        var l1 = evaluator.LegLength(1);
        var l2 = evaluator.LegLength(2);
        AssertTimes([0f, 1f, 1f + (l1 / 3f), 1f + (l1 / 3f) + (l2 / 7f)], result);
        Assert.Equal(new[] { 0f, 0f, 1f, 2f }, evaluator.Keys.Select(k => k.Position));
    }

    [Fact]
    public void MovingAPointToWhereItIsChangesNothing()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.Reorder(track, [0, 1, 2]));
    }

    [Fact]
    public void ReplaceKeepsEveryTimingKey()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var point = Point(99f, 1f, 2f);
        var result = TrackEditing.Replace(track, 1, point);

        Assert.Same(point, result.Points[1]);
        Assert.Same(track.Timing, result.Timing);
    }

    [Fact]
    public void ReplaceWithAnEqualPointReturnsTheSameTrack()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.Replace(track, 1, track.Points[1] with { });

        Assert.Same(track, result);
    }

    [Fact]
    public void EditsRejectOutOfRangeIndices()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.InsertAfter(track, 3, Point(0f, 0f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Delete(track, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Replace(track, 3, Point(0f, 0f, 0f)));
    }

    [Fact]
    public void AddingAHoldMovesTheNextLegsEasingOntoTheHoldEnd()
    {
        var track = LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn);
        track = TrackEditing.SetHold(track, 1, 2f);
        var keys = Keys(track);

        Assert.Equal(TangentMode.Auto, keys[1].OutMode);
        Assert.Equal(TangentMode.Flat, keys[2].OutMode);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void RemovingAHoldMovesTheEasingBack()
    {
        var track = TrackEditing.SetHold(LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn), 1, 2f);
        track = TrackEditing.SetHold(track, 1, 0f);

        Assert.Equal(3, Keys(track).Count);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void KeyRolesTellPointsAndHoldEndsApart()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);

        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 0));
        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 1));
        Assert.Equal(KeyRole.HoldEnd, TrackEditing.RoleOf(track, 2));
        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 3));
        Assert.Equal(new[] { 0, 1, 1, 2 }, Enumerable.Range(0, 4).Select(k => TrackEditing.PointOf(track, k)));
        Assert.Equal(1, TrackEditing.PointKey(track, 1));
        Assert.Equal(3, TrackEditing.PointKey(track, 2));
        Assert.Equal(2, TrackEditing.LegStartKey(track, 2));
        Assert.Equal(3, TrackEditing.LegEndKey(track, 2));
    }

    [Fact]
    public void LegLengthsAreIndexedByLeg()
    {
        var lengths = TrackEditing.LegLengths(Build3PointTrack());
        Assert.Equal(3, lengths.Length);
        Assert.Equal(0f, lengths[0]);
        Assert.Equal(10f, lengths[1], 1);
        Assert.Equal(10f, lengths[2], 1);
    }

    [Fact]
    public void ReorderKeepsEasingInItsSlot()
    {
        var track = LegEasing.Set(Build3PointTrack(), 1, Easing.EaseOut);
        track = TrackEditing.SetHold(track, 2, 3f);
        track = TrackEditing.Reorder(track, [2, 0, 1]);

        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 1));
        Assert.Equal(new Vector3(20f, 0f, 0f), track.Points[0].Position);
    }

    private static float OutSlope(Track track, int key) => new TrackEvaluator(track).SideSlope(key, KeySide.Out);

    // Build3PointTrack with point 1's handles at half the average speed on both sides.
    private static Track HalfSpeedHandles() => TimingEditing.SetHandles(Build3PointTrack(), 1, 0.5f, 0.5f);

    [Fact]
    public void AHandleIsHalfItsSpansAverageSpeed() => Assert.Equal(1f, OutSlope(HalfSpeedHandles(), 1), 3);

    [Fact]
    public void AHandleKeepsItsShapeThroughTheTrackSpeed() =>
        Assert.Equal(2f, OutSlope(TrackEditing.SetSpeed(HalfSpeedHandles(), 4f), 1), 3);

    [Fact]
    public void AHandleKeepsItsShapeThroughALegDuration()
    {
        // Leg 2's duration drops to 2 s (was 5 s), so its average speed becomes 10/2 = 5 (leg 1 stays 10/5 = 2).
        // Key 1's stored ratios stay 0.5 each: rawIn = 0.5 * 2 = 1, rawOut = 0.5 * 5 = 2.5. Unbroken and both Manual,
        // it takes their average, 1.75, within the slower leg's bound of 3 * 2 = 6, so both sides read 1.75.
        Assert.Equal(1.75f, OutSlope(TrackEditing.SetLegDuration(HalfSpeedHandles(), 2, 2f), 1), 3);
    }

    [Fact]
    public void AHandleKeepsItsShapeThroughAPointEdit() =>
        Assert.Equal(1f, OutSlope(TrackEditing.Replace(HalfSpeedHandles(), 2, Point(40f, 0f, 0f)), 1), 3);

    [Fact]
    public void InsertingOnANeighbouringLegKeepsTheRatio()
    {
        var result = TrackEditing.InsertAfter(HalfSpeedHandles(), 0, Point(4f, 0f, 0f));

        Assert.Equal((0.5f, 0.5f), (result.Timing[2].InTangent, result.Timing[2].OutTangent));
        var evaluator = new TrackEvaluator(result);
        Assert.Equal(1f, evaluator.SideSlope(2, KeySide.In), 3);
        Assert.Equal(1f, evaluator.SideSlope(2, KeySide.Out), 3);
    }

    [Fact]
    public void DeletingOnANeighbouringLegKeepsTheRatio()
    {
        var track = TrackEditing.InsertAfter(HalfSpeedHandles(), 0, Point(4f, 0f, 0f));

        var result = TrackEditing.Delete(track, 1);

        Assert.Equal((0.5f, 0.5f), (result.Timing[1].InTangent, result.Timing[1].OutTangent));
        var evaluator = new TrackEvaluator(result);
        Assert.Equal(1f, evaluator.SideSlope(1, KeySide.In), 3);
        Assert.Equal(1f, evaluator.SideSlope(1, KeySide.Out), 3);
    }

    [Fact]
    public void DeletingSeveralPointsMergesTheirLegsAsSingleDeletesWould()
    {
        var track = TrackEditing.Append(Build3PointTrack(), Point(30f));
        track = TrackEditing.SetLegSpeed(track, 1, 3f);
        track = TrackEditing.SetLegSpeed(track, 2, 5f);
        track = TrackEditing.SetLegSpeed(track, 3, 7f);

        // Point 2 goes first: the leg to point 3 takes leg 2's 5. Then point 1: that leg takes leg 1's 3.
        var result = TrackEditing.Delete(track, [1, 2]);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(0f, result.Points[0].Position.X, 0.0001f);
        Assert.Equal(30f, result.Points[1].Position.X, 0.0001f);
        Assert.Equal(3f, TrackEditing.LegSpeed(result, 1), 0.0001f);
    }

    [Fact]
    public void DeletingSeveralRefusesAnIndexOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Delete(Build3PointTrack(), [0, 3]));

    [Fact]
    public void ReorderingTwoEqualPointsStillSwapsTheirHolds()
    {
        var track = TrackEditing.Append(TrackEditing.Append(TrackEditing.Empty(), Point(0f)), Point(0f));
        track = TrackEditing.SetHold(track, 0, 2f);

        var result = TrackEditing.Reorder(track, [1, 0]);

        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 0), 0.0001f);
        Assert.Equal(2f, TrackEditing.HoldSeconds(result, 1), 0.0001f);
    }

    // Follow Target takes a track with at most one point; every other mode takes any track.

    [Fact]
    public void FollowTargetRefusesATrackWithMoreThanOnePoint()
    {
        var two = WithTwoPoints(TrackEditing.Empty());
        var one = TrackEditing.Append(TrackEditing.Empty(), Point(0f));

        Assert.Equal("Follow Target needs a track with one point.", TrackEditing.AimRefusal(two, AimMode.FollowTarget));
        Assert.Null(TrackEditing.AimRefusal(one, AimMode.FollowTarget));
        Assert.Null(TrackEditing.AimRefusal(two, AimMode.LookAt));
    }

    // The camera places the Look At point only for Look At, with no points and no point placed yet.

    [Fact]
    public void OnlyTheFirstLookAtOnAnEmptyTrackPlacesFromTheCamera()
    {
        Assert.True(TrackEditing.LookAtFromCamera(TrackEditing.Empty(), AimMode.LookAt));
        Assert.True(TrackEditing.LookAtFromCamera(TrackEditing.Empty(AimMode.LookAt), AimMode.LookAt));
        Assert.False(TrackEditing.LookAtFromCamera(TrackEditing.Empty() with { LookAtPlaced = true }, AimMode.LookAt));
        Assert.False(
            TrackEditing.LookAtFromCamera(TrackEditing.Append(TrackEditing.Empty(), Point(0f)), AimMode.LookAt)
        );
        Assert.False(TrackEditing.LookAtFromCamera(TrackEditing.Empty(), AimMode.AimKeys));
    }
}
