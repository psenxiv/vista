using System.Linq;
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class TrackEditingTests
{
    private static IReadOnlyList<TimingKey> Keys(Track track) => new TrackEvaluator(track).Keys;

    private static float LegSeconds(Track track, int leg) => new TrackEvaluator(track).LegSeconds(leg);

    private static float Total(Track track) => (float)new TrackEvaluator(track).Duration;

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
        var track = TrackEditing.Append(TrackEditing.Empty() with { Anchor = anchor, AnchorPlaced = true }, Point(1f, 2f, 3f));
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
    public void LegSecondsReadsTheCurrentGapBetweenPoints()
    {
        var track = Build3PointTrack();
        Assert.Equal(5f, LegSeconds(track, 1), 3);
        Assert.Equal(5f, LegSeconds(track, 2), 3);
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
    }

    [Fact]
    public void SetLegDurationRejectsLegZero()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLegDuration(track, 0, 5f));
    }

    [Fact]
    public void SetLegDurationRejectsAnIndexAtOrAboveThePointCount()
    {
        var track = Build3PointTrack(); // 3 points; valid legs are 1..2
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLegDuration(track, 3, 5f));
    }

    [Fact]
    public void RejectionMessagesAreOnlyThePlainMessage()
    {
        var track = Build3PointTrack();
        Assert.Equal("leg index must be 1..2 for a 3-point track", Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLegDuration(track, 3, 1f)).Message);
        Assert.Equal("hold index must be 0..2 for a 3-point track", Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 3, 1f)).Message);
    }

    [Fact]
    public void SetLegDurationClampsNonPositiveSecondsToTheShortestLeg()
    {
        var track = Build3PointTrack();
        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(TrackEditing.SetLegDuration(track, 1, 0f), 1), 3);
        Assert.Equal(TrackEditing.MinLegSeconds, LegSeconds(TrackEditing.SetLegDuration(track, 1, -1f), 1), 3);
    }

    [Fact]
    public void SetLegDurationIgnoresNaNAndClampsInfinity()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.SetLegDuration(track, 1, float.NaN));
        Assert.Equal(TrackEditing.MaxSeconds, LegSeconds(TrackEditing.SetLegDuration(track, 1, float.PositiveInfinity), 1), 1);
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

        Assert.Equal(4, Keys(track).Count);
        Assert.Equal(3f, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(13f, Keys(track)[^1].Time, 3);
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
    public void SetHoldIgnoresNaNAndClampsInfinity()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.SetHold(track, 1, float.NaN));
        Assert.Equal(TrackEditing.MaxSeconds, TrackEditing.HoldSeconds(TrackEditing.SetHold(track, 1, float.PositiveInfinity), 1));
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
        Assert.Equal("this track has no legs", ex.Message);
    }

    [Fact]
    public void HoldOnAnEmptyTrackSaysItHasNoPoints()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(TrackEditing.Empty(), 0));
        Assert.Equal("this track has no points", ex.Message);
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
        track = TrackEditing.SetSpeed(TrackEditing.SetLoop(TrackEditing.SetDirection(track, PlaybackDirection.Reverse), true), 7f);
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
    public void PointSecondsIsWhenThePointIsReached()
    {
        var evaluator = new TrackEvaluator(TrackEditing.SetHold(Build3PointTrack(), 1, 2f));
        Assert.Equal(0f, evaluator.PointSeconds(0));
        Assert.Equal(5f, evaluator.PointSeconds(1), 3);
        Assert.Equal(12f, evaluator.PointSeconds(2), 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.PointSeconds(3));
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
    public void AHandleIsHalfItsSpansAverageSpeed()
        => Assert.Equal(1f, OutSlope(HalfSpeedHandles(), 1), 3);

    [Fact]
    public void AHandleKeepsItsShapeThroughTheTrackSpeed()
        => Assert.Equal(2f, OutSlope(TrackEditing.SetSpeed(HalfSpeedHandles(), 4f), 1), 3);

    [Fact]
    public void AHandleKeepsItsShapeThroughALegDuration()
        => Assert.Equal(2.5f, OutSlope(TrackEditing.SetLegDuration(HalfSpeedHandles(), 2, 2f), 1), 3);

    [Fact]
    public void AHandleKeepsItsShapeThroughAPointEdit()
        => Assert.Equal(1f, OutSlope(TrackEditing.Replace(HalfSpeedHandles(), 2, Point(40f, 0f, 0f)), 1), 3);

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

        Assert.Equal(new[] { 0f, 30f }, result.Points.Select(p => p.Position.X));
        Assert.Equal(3f, TrackEditing.LegSpeed(result, 1));
    }

    [Fact]
    public void DeletingSeveralRefusesAnIndexOutOfRange()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Delete(Build3PointTrack(), [0, 3]));
}
