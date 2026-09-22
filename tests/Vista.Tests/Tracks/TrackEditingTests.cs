using System.Linq;
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class TrackEditingTests
{
    private static ControlPoint Point(float x, float y, float z)
        => new(new Vector3(x, y, z), 0f, 0f, 1f);

    // Points at 0,10,20 with default legs (5s each): keys at times 0, 5, 10.
    private static Track Build3PointTrack()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        track = TrackEditing.Append(track, Point(20f, 0f, 0f));
        return track;
    }

    [Fact]
    public void EmptyHasNoPointsNoKeysAndPlaysOnce()
    {
        var track = TrackEditing.Empty();

        Assert.Empty(track.Points);
        Assert.Empty(track.Timing);
        Assert.Equal(PlaybackMode.Once, track.Playback);
        Assert.Equal(AimMode.AimKeys, track.Aim);
    }

    [Fact]
    public void EmptyAcceptsAnExplicitAimMode()
    {
        var track = TrackEditing.Empty(AimMode.PathTangent);
        Assert.Equal(AimMode.PathTangent, track.Aim);
    }

    [Fact]
    public void AppendFirstPointGetsKeyAtTimeZero()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(1f, 2f, 3f));

        Assert.Single(track.Points);
        Assert.Single(track.Timing);
        Assert.Equal(0f, track.Timing[0].Time);
        Assert.Equal(0f, track.Timing[0].Position);
        Assert.Equal(TangentMode.Auto, track.Timing[0].InMode);
    }

    [Fact]
    public void AppendLaterPointGetsKeyOneDefaultLegAfterThePreviousLastKey()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));

        Assert.Equal(2, track.Timing.Count);
        Assert.Equal(TrackEditing.DefaultLegSeconds, track.Timing[1].Time);
        Assert.Equal(1f, track.Timing[1].Position);
    }

    [Fact]
    public void AppendAfterAHoldPlacesTheNewKeyOneLegAfterTheHoldKey()
    {
        var track = Build3PointTrack(); // keys at 0, 5, 10
        track = TrackEditing.SetHold(track, 2, 4f); // point 2's second key lands at 14

        track = TrackEditing.Append(track, Point(30f, 0f, 0f));

        Assert.Equal(4, track.Points.Count);
        Assert.Equal(5, track.Timing.Count);
        Assert.Equal(3f, track.Timing[^1].Position);
        Assert.Equal(14f + TrackEditing.DefaultLegSeconds, track.Timing[^1].Time, 5);
    }

    [Fact]
    public void AppendingAControlPointDoesNotRetimeTheExistingOnes()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        track = TrackEditing.SetHold(track, 1, 2f);

        var keysBefore = track.Timing.ToArray();
        var evaluatorBefore = new TrackEvaluator(track);
        var positionsBefore = keysBefore.Select(k => evaluatorBefore.Evaluate(k.Time)!.Value.Position).ToArray();

        track = TrackEditing.Append(track, Point(20f, 0f, 0f));

        Assert.Equal(keysBefore, track.Timing.Take(keysBefore.Length));

        var evaluatorAfter = new TrackEvaluator(track);
        for (var i = 0; i < keysBefore.Length; i++)
            Assert.Equal(positionsBefore[i], evaluatorAfter.Evaluate(keysBefore[i].Time)!.Value.Position);
    }

    [Fact]
    public void LegSecondsReadsTheCurrentGapBetweenPoints()
    {
        var track = Build3PointTrack();
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 1));
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 2));
    }

    [Fact]
    public void SetLegShiftsLaterKeysByTheDifference()
    {
        var track = Build3PointTrack(); // times 0, 5, 10
        track = TrackEditing.SetLeg(track, 1, 8f);

        Assert.Equal(8f, track.Timing[1].Time, 5);
        Assert.Equal(13f, track.Timing[2].Time, 5);
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 2), 5);
    }

    [Fact]
    public void SetLegRejectsLegZero()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 0, 5f));
    }

    [Fact]
    public void SetLegRejectsAnIndexAtOrAboveThePointCount()
    {
        var track = Build3PointTrack(); // 3 points; valid legs are 1..2
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 3, 5f));
    }

    [Fact]
    public void RejectionMessagesAreOnlyThePlainMessage()
    {
        var track = Build3PointTrack();
        Assert.Equal("leg seconds must be > 0", Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 1, 0f)).Message);
        Assert.Equal("hold index must be 0..2 for a 3-point track", Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 3, 1f)).Message);
    }

    [Fact]
    public void SetLegRejectsNonPositiveSeconds()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 1, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 1, -1f));
    }

    [Fact]
    public void SetLegRejectsNonFiniteSeconds()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 1, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetLeg(track, 1, float.PositiveInfinity));
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

        Assert.Equal(4, track.Timing.Count);
        Assert.Equal(3f, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(13f, track.Timing[^1].Time, 5);
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 2), 5);
    }

    [Fact]
    public void SetHoldResizesAnExistingHold()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        track = TrackEditing.SetHold(track, 1, 7f);

        Assert.Equal(4, track.Timing.Count);
        Assert.Equal(7f, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 2), 5);
    }

    [Fact]
    public void SetHoldZeroRemovesTheSecondKey()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        track = TrackEditing.SetHold(track, 1, 0f);

        Assert.Equal(3, track.Timing.Count);
        Assert.Equal(0f, TrackEditing.HoldSeconds(track, 1));
        Assert.Equal(5f, TrackEditing.LegSeconds(track, 2), 5);
    }

    [Fact]
    public void SetHoldRejectsNegativeSeconds()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 1, -1f));
    }

    [Fact]
    public void SetHoldRejectsNonFiniteSeconds()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 1, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.SetHold(track, 1, float.PositiveInfinity));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.LegSeconds(track, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(track, 3));
    }

    [Fact]
    public void LegOnATrackWithFewerThanTwoPointsSaysItHasNoLegs()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f, 0f, 0f));
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.LegSeconds(track, 1));
        Assert.Equal("this track has no legs", ex.Message);
    }

    [Fact]
    public void HoldOnAnEmptyTrackSaysItHasNoPoints()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(TrackEditing.Empty(), 0));
        Assert.Equal("this track has no points", ex.Message);
    }

    [Fact]
    public void APointWithNoTimingKeyIsRefusedAsAnArgumentError()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[] { new TimingKey(0f, 0f, TangentMode.Auto, TangentMode.Auto, 0f, 0f) };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        var leg = Assert.Throws<ArgumentException>(() => TrackEditing.LegSeconds(track, 1));
        Assert.Equal("point 1 has no timing key", leg.Message);
        var hold = Assert.Throws<ArgumentException>(() => TrackEditing.SetHold(track, 1, 2f));
        Assert.Equal("point 1 has no timing key", hold.Message);
    }

    [Fact]
    public void SettingALegAfterAHoldKeepsTheHold()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 3f);
        var holdBefore = TrackEditing.HoldSeconds(track, 1);

        track = TrackEditing.SetLeg(track, 2, 8f);

        Assert.Equal(holdBefore, TrackEditing.HoldSeconds(track, 1), 5);
        Assert.Equal(8f, TrackEditing.LegSeconds(track, 2), 5);
    }

    [Fact]
    public void GeneratedKeysUseAutoTangentsWithZeroTangents()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetHold(track, 1, 2f);

        foreach (var key in track.Timing)
        {
            Assert.Equal(TangentMode.Auto, key.InMode);
            Assert.Equal(0f, key.InTangent);
            Assert.Equal(0f, key.OutTangent);
        }
    }

    [Fact]
    public void SetPlaybackChangesModeWithoutTouchingPointsOrKeys()
    {
        var track = Build3PointTrack();
        var updated = TrackEditing.SetPlayback(track, PlaybackMode.Loop);

        Assert.Equal(PlaybackMode.Loop, updated.Playback);
        Assert.Equal(track.Points, updated.Points);
        Assert.Equal(track.Timing, updated.Timing);
    }

    private static float Total(Track track) => track.Timing[^1].Time;

    [Fact]
    public void InsertAfterSplitsTheLegByPathLengthAndKeepsTheTotal()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 0, Point(2f, 0f, 0f));

        var table = new ArcLengthTable(result.Points.Select(p => p.Position).ToArray());
        var before = table.SegmentLength(0);
        var after = table.SegmentLength(1);

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(5f * before / (before + after), TrackEditing.LegSeconds(result, 1), 3);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 1) + TrackEditing.LegSeconds(result, 2), 4);
        Assert.Equal(Total(track), Total(result), 4);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 3), 4);
    }

    [Fact]
    public void InsertAfterKeepsTheSelectedPointsHoldAndGivesTheNewPointNone()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 2f);
        var result = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));

        Assert.Equal(2f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 1));
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void InsertAfterTheLastPointAppends()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 2, Point(30f, 0f, 0f));

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(TrackEditing.DefaultLegSeconds, TrackEditing.LegSeconds(result, 3));
    }

    [Fact]
    public void InsertBetweenCoincidentPointsSplitsEvenly()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        var result = TrackEditing.InsertAfter(track, 0, Point(0f, 0f, 0f));

        Assert.Equal(2.5f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(2.5f, TrackEditing.LegSeconds(result, 2), 4);
    }

    [Fact]
    public void DeletingAMiddlePointMergesItsLegsAndHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var result = TrackEditing.Delete(track, 1);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(12f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void DeletingTheFirstPointDropsItsHoldAndLeg()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 1f);
        var result = TrackEditing.Delete(track, 0);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(0f, result.Timing[0].Time);
        Assert.Equal(0f, result.Timing[0].Position);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(5f, Total(result), 4);
    }

    [Fact]
    public void DeletingTheLastPointDropsItsLegAndHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 2, 2f);
        var result = TrackEditing.Delete(track, 2);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(5f, Total(result), 4);
    }

    [Fact]
    public void DeletingTheOnlyPointEmptiesTheTrackButKeepsItsModes()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent), Point(0f, 0f, 0f));
        track = TrackEditing.SetPlayback(track, PlaybackMode.Loop);
        var result = TrackEditing.Delete(track, 0);

        Assert.Empty(result.Points);
        Assert.Empty(result.Timing);
        Assert.Equal(AimMode.PathTangent, result.Aim);
        Assert.Equal(PlaybackMode.Loop, result.Playback);
    }

    [Fact]
    public void MovingAPointCarriesItsHoldAndLeavesLegsInTheirSlots()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetLeg(track, 1, 3f);
        track = TrackEditing.SetLeg(track, 2, 7f);
        track = TrackEditing.SetHold(track, 2, 1f);
        var moved = track.Points[2];

        var result = TrackEditing.Move(track, 2, 0);

        Assert.Same(moved, result.Points[0]);
        Assert.Same(track.Points[0], result.Points[1]);
        Assert.Same(track.Points[1], result.Points[2]);
        Assert.Equal(1f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 2));
        Assert.Equal(3f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(7f, TrackEditing.LegSeconds(result, 2), 4);
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void MovingAPointToWhereItIsChangesNothing()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.Move(track, 1, 1));
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
    public void EditsRefuseAPointWithNoTimingKey()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[] { new TimingKey(0f, 0f, TangentMode.Auto, TangentMode.Auto, 0f, 0f) };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        const string message = "every point needs one or two timing keys";
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f))).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Delete(track, 0)).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Move(track, 0, 1)).Message);
    }

    [Fact]
    public void EditsRefuseAPointWithMoreThanTwoTimingKeys()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[]
        {
            new TimingKey(0f, 0f, TangentMode.Auto, TangentMode.Auto, 0f, 0f),
            new TimingKey(1f, 0f, TangentMode.Auto, TangentMode.Auto, 0f, 0f),
            new TimingKey(2f, 0f, TangentMode.Auto, TangentMode.Auto, 0f, 0f),
            new TimingKey(7f, 1f, TangentMode.Auto, TangentMode.Auto, 0f, 0f),
        };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        const string message = "every point needs one or two timing keys";
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f))).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Delete(track, 0)).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Move(track, 0, 1)).Message);
    }

    [Fact]
    public void EditsRejectOutOfRangeIndices()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.InsertAfter(track, 3, Point(0f, 0f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Delete(track, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Move(track, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Replace(track, 3, Point(0f, 0f, 0f)));
    }

    [Fact]
    public void PointSecondsIsWhenThePointIsReached()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.PointSeconds(track, 0));
        Assert.Equal(5f, TrackEditing.PointSeconds(track, 1));
        Assert.Equal(12f, TrackEditing.PointSeconds(track, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.PointSeconds(track, 3));
    }

    [Fact]
    public void AddingAHoldMovesTheNextLegsEasingOntoTheHoldEnd()
    {
        var track = LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn);
        track = TrackEditing.SetHold(track, 1, 2f);

        Assert.Equal(TangentMode.Auto, track.Timing[1].OutMode);
        Assert.Equal(TangentMode.Flat, track.Timing[2].OutMode);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void RemovingAHoldMovesTheEasingBack()
    {
        var track = TrackEditing.SetHold(LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn), 1, 2f);
        track = TrackEditing.SetHold(track, 1, 0f);

        Assert.Equal(3, track.Timing.Count);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void KeyRolesTellPointsHoldEndsAndInnerKeysApart()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var timing = track.Timing.ToList();
        timing.Insert(1, new TimingKey(2.5f, 0.5f));
        track = track with { Timing = timing };

        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 0));
        Assert.Equal(KeyRole.Inner, TrackEditing.RoleOf(track, 1));
        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 2));
        Assert.Equal(KeyRole.HoldEnd, TrackEditing.RoleOf(track, 3));
        Assert.Equal(2, TrackEditing.PointKey(track, 1));
        Assert.Equal(3, TrackEditing.LegStartKey(track, 2));
        Assert.Equal(4, TrackEditing.LegEndKey(track, 2));
    }

    [Fact]
    public void LegAtFindsTheLegAndSkipsHolds()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys at 0, 5, 7, 12
        Assert.Equal(1, TrackEditing.LegAt(track, 2f));
        Assert.Null(TrackEditing.LegAt(track, 6f));
        Assert.Equal(2, TrackEditing.LegAt(track, 9f));
        Assert.Null(TrackEditing.LegAt(track, 13f));
    }

    // Adds a key at the given time and position, keeping keys in time order.
    private static Track WithInner(Track track, float time, float position)
    {
        var timing = track.Timing.Append(new TimingKey(time, position)).OrderBy(k => k.Time).ToList();
        return track with { Timing = timing };
    }

    [Fact]
    public void SetLegSpreadsInnerKeysInProportion()
    {
        var track = WithInner(Build3PointTrack(), 2f, 0.3f);        // leg 1 is 0..5 s
        track = TrackEditing.SetLeg(track, 1, 10f);

        Assert.Equal(new[] { 0f, 4f, 10f, 15f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.3f, track.Timing[1].Position);
    }

    [Fact]
    public void SetLegClampsUpSoInnerKeysKeepTheirSpacing()
    {
        var track = WithInner(Build3PointTrack(), 0.5f, 0.3f);      // spans 0.5 and 4.5 s
        track = TrackEditing.SetLeg(track, 1, 0.1f);

        Assert.Equal(0.5f, TrackEditing.LegSeconds(track, 1), 3);    // the 0.5 s span may only shrink to 0.05 s
        Assert.Equal(TrackEditing.MinLegSeconds, TrackEditing.MinLegFor(Build3PointTrack(), 1));
    }

    [Fact]
    public void AddAfterSelectedSortsInnerKeysIntoTheHalvesByTime()
    {
        // Points at x = 0, 10, 20. Inserting (5, 0, 0) after point 0 splits leg 1 (0..5 s) at 2.5 s, half the distance.
        var track = WithInner(WithInner(Build3PointTrack(), 1f, 0.2f), 4f, 0.8f);
        track = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));

        Assert.Equal(new[] { 0f, 1f, 2.5f, 4f, 5f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.4f, track.Timing[1].Position, 2);
        Assert.Equal(1f, track.Timing[2].Position);
        Assert.Equal(1.6f, track.Timing[3].Position, 2);
        Assert.Equal(2f, track.Timing[4].Position);
        Assert.Equal(3f, track.Timing[5].Position);
    }

    [Fact]
    public void AddAfterSelectedDropsAnInnerKeyTooCloseToTheNewPoint()
    {
        var track = WithInner(Build3PointTrack(), 2.52f, 0.5f);
        track = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));
        Assert.Equal(new[] { 0f, 2.5f, 5f, 10f }, track.Timing.Select(k => k.Time));
    }

    [Fact]
    public void AddAfterSelectedKeepsTheLegsEasingOnItsOuterSides()
    {
        var track = TrackEditing.InsertAfter(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut), 0, Point(5f, 0f, 0f));
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 1));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 2));
    }

    [Fact]
    public void DeletingAMiddlePointMergesBothLegsInnerKeysByDistance()
    {
        // Legs 0..5 and 5..10 s, each 10 long; merged, point 1's place sits at half the distance.
        var track = WithInner(WithInner(Build3PointTrack(), 2f, 0.5f), 7f, 1.5f);
        track = TrackEditing.Delete(track, 1);

        Assert.Equal(new[] { 0f, 2f, 7f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.25f, track.Timing[1].Position, 2);
        Assert.Equal(0.75f, track.Timing[2].Position, 2);
        Assert.Equal(1f, track.Timing[3].Position);
    }

    [Fact]
    public void DeletingAMiddlePointKeepsTheMergedLegsOuterEasing()
    {
        var track = LegEasing.Set(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseIn), 2, Easing.EaseOut);
        Assert.Equal(Easing.EaseInOut, LegEasing.Read(TrackEditing.Delete(track, 1), 1));
    }

    [Fact]
    public void DeletingAnEndPointTakesItsLegsInnerKeys()
    {
        var first = TrackEditing.Delete(WithInner(Build3PointTrack(), 2f, 0.5f), 0);
        Assert.Equal(new[] { 0f, 5f }, first.Timing.Select(k => k.Time));

        var last = TrackEditing.Delete(WithInner(Build3PointTrack(), 7f, 1.5f), 2);
        Assert.Equal(new[] { 0f, 5f }, last.Timing.Select(k => k.Time));
    }

    [Fact]
    public void ReorderKeepsLegTimesEasingAndInnerKeysInTheirSlots()
    {
        var track = LegEasing.Set(WithInner(Build3PointTrack(), 2f, 0.5f), 1, Easing.EaseOut);
        track = TrackEditing.SetHold(track, 2, 3f);                  // point 2 holds 10..13 s
        track = TrackEditing.Move(track, 2, 0);

        Assert.Equal(new[] { 0f, 3f, 5f, 8f, 13f }, track.Timing.Select(k => k.Time));
        Assert.Equal(new[] { 0f, 0f, 0.5f, 1f, 2f }, track.Timing.Select(k => k.Position));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 1));
        Assert.Equal(new Vector3(20f, 0f, 0f), track.Points[0].Position);
    }

    private static Track Track(float[] xs, params TimingKey[] keys)
    {
        var points = xs.Select(x => Point(x, 0f, 0f)).ToList();
        return new Track(points, keys, AimMode.AimKeys, PlaybackMode.Once);
    }

    private static TimingKey Manual(float time, float position, float inSlope, float outSlope)
        => new(time, position, TangentMode.Manual, TangentMode.Manual, inSlope, outSlope);

    private static int KeyAt(Track track, float time)
        => track.Timing.Select((k, i) => (k, i)).Single(p => MathF.Abs(p.k.Time - time) < 1e-4f).i;

    private static void AssertSlopesKept(Track before, Track after, params (float Time, KeySide Side)[] sides)
    {
        var was = new TrackEvaluator(before);
        var now = new TrackEvaluator(after);
        foreach (var (time, side) in sides)
            Assert.Equal(was.SideSlope(KeyAt(before, time), side), now.SideSlope(KeyAt(after, time), side), 3);
    }

    [Fact]
    public void InsertAfterKeepsManualSlopesInDistancePerSecond()
    {
        // Leg 0..10 m over 0..5 s, split at x = 4 (share 0.4); every secant is 2 m/s and every Manual side 2 or 2.5 m/s.
        var track = Track(
            new[] { 0f, 10f, 20f },
            new TimingKey(0f, 0f, OutMode: TangentMode.Manual, OutTangent: 0.2f),
            Manual(1f, 0.2f, 0.25f, 0.25f),
            Manual(4f, 0.8f, 0.2f, 0.2f),
            new TimingKey(5f, 1f, InMode: TangentMode.Manual, InTangent: 0.2f),
            new TimingKey(10f, 2f));

        var result = TrackEditing.InsertAfter(track, 0, Point(4f, 0f, 0f));

        AssertSlopesKept(track, result, (0f, KeySide.Out), (1f, KeySide.In), (1f, KeySide.Out), (4f, KeySide.In), (4f, KeySide.Out), (5f, KeySide.In));
    }

    [Fact]
    public void DeletingAMiddlePointKeepsManualSlopesInDistancePerSecond()
    {
        // Legs of 6 and 14 m merge into one of 20 m; every Manual side is 2 m/s, at most twice its secant.
        var track = Track(
            new[] { 0f, 6f, 20f },
            new TimingKey(0f, 0f, OutMode: TangentMode.Manual, OutTangent: 2f / 6f),
            Manual(2f, 0.5f, 2f / 6f, 2f / 6f),
            new TimingKey(5f, 1f),
            Manual(7f, 1.5f, 2f / 14f, 2f / 14f),
            new TimingKey(10f, 2f, InMode: TangentMode.Manual, InTangent: 2f / 14f));

        var result = TrackEditing.Delete(track, 1);

        AssertSlopesKept(track, result, (0f, KeySide.Out), (2f, KeySide.In), (2f, KeySide.Out), (7f, KeySide.In), (7f, KeySide.Out), (10f, KeySide.In));
    }

    [Fact]
    public void DeletingAMiddlePointNextToATinySegmentKeepsInnerKeysInside()
    {
        // Leg 2 is 0.1 m against a leg 3 of a million, so its inner key would merge onto point 1's place.
        var track = Track(
            new[] { 0f, 10f, 10.05f, 1_000_000f },
            new TimingKey(0f, 0f),
            new TimingKey(5f, 1f),
            new TimingKey(6f, 1.5f),
            new TimingKey(10f, 2f),
            new TimingKey(15f, 3f));

        var result = TrackEditing.Delete(track, 2);

        var inner = KeyAt(result, 6f);
        Assert.True(result.Timing[inner].Position > 1f);
        Assert.Equal(KeyRole.Inner, TrackEditing.RoleOf(result, inner));
    }
}
