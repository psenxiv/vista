using CsCheck;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimingEditingTests
{
    private static Track MoveKey(Track track, int key, float time) =>
        TimingEditing.MoveKey(track, new TrackEvaluator(track), key, time);

    private static float[] Times(Track track) =>
        new TrackEvaluator(track).Keys.Select(k => MathF.Round(k.Time, 2)).ToArray();

    private static Track RippleKey(Track track, int key, float time) =>
        TimingEditing.RippleKey(track, new TrackEvaluator(track), key, time);

    [Fact]
    public void SetKeyModeSetsBothSidesAndRefusesManual()
    {
        var track = TimingEditing.SetKeyMode(Build3PointTrack(), 1, TangentMode.Linear);
        Assert.Equal((TangentMode.Linear, TangentMode.Linear), (track.Timing[1].InMode, track.Timing[1].OutMode));
        Assert.Equal(
            "A key's mode can't be set to Manual directly.",
            Assert.Throws<ArgumentException>(() => TimingEditing.SetKeyMode(track, 1, TangentMode.Manual)).Message
        );
    }

    [Fact]
    public void SettingTheSameKeyModeReturnsTheSameTrack()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TimingEditing.SetKeyMode(track, 1, TangentMode.Auto));
    }

    [Fact]
    public void MovingAPointKeySqueezesItsNeighboursAndKeepsTheLength()
    {
        var track = MoveKey(Build3PointTrack(), 1, 3f);
        Assert.Equal(new[] { 0f, 3f, 10f }, Times(track));
        Assert.Equal(1f, new TrackEvaluator(track).Keys[1].Position);
        Assert.True(TrackEditing.IsPinned(track, 1));
        Assert.True(TrackEditing.IsPinned(track, 2));
    }

    [Fact]
    public void APointKeyDragClampsToTheShortestLegs()
    {
        var evaluator = new TrackEvaluator(MoveKey(Build3PointTrack(), 1, -4f));
        Assert.Equal(TrackEditing.MinLegSeconds, evaluator.Keys[1].Time, 2);
        evaluator = new TrackEvaluator(MoveKey(Build3PointTrack(), 1, 50f));
        Assert.Equal(10f - TrackEditing.MinLegSeconds, evaluator.Keys[1].Time, 2);
    }

    [Fact]
    public void TheFirstKeyNeverMovesAndTheLastKeyChangesTheLength()
    {
        var track = Build3PointTrack();
        Assert.Same(track, MoveKey(track, 0, 2f));
        Assert.Same(track, MoveKey(track, 1, float.NaN));
        Assert.Same(track, MoveKey(track, 1, float.PositiveInfinity));
        Assert.Same(track, MoveKey(track, 2, float.NegativeInfinity));

        var longer = MoveKey(track, 2, 12f);
        Assert.Equal(12f, new TrackEvaluator(longer).Keys[2].Time, 2);
        Assert.False(TrackEditing.IsPinned(longer, 1));
    }

    [Fact]
    public void AHoldEndDragChangesTheHoldAndShiftsLaterKeys()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f); // keys 0, 5, 7, 12
        track = MoveKey(track, 2, 8f);
        Assert.Equal(new[] { 0f, 5f, 8f, 13f }, Times(track));
        Assert.Equal(1f, new TrackEvaluator(track).Keys[2].Position);
        Assert.Equal(3f, TrackEditing.HoldSeconds(track, 1), 2);
    }

    [Fact]
    public void DraggingAKeyOntoItsHoldEndKeepsTheShortestHold()
    {
        // At 0.37 y/s the 10-yalm legs take 10 / 0.37 = 27.027027 s, so with a 1.3 s hold the keys sit at 0, 27.027027,
        // 28.327026 and 55.354053. Dragged past the hold end, key 1 stops MinKeyGap short of it: 28.327026 - 0.05 = 28.277026.
        // Between 16 and 32 s floats are 2^-19 s apart and 0.05 s is 26214.4 of those steps, so key 1 lands 26214 steps short,
        // 0.04999924 s, which SetHold would round to no hold. The hold stays at its shortest, MinKeyGap, and every key stays.
        var track = TrackEditing.SetHold(TrackEditing.SetSpeed(Build3PointTrack(), 0.37f), 1, 1.3f);

        var moved = MoveKey(track, 1, 1000f);

        Assert.Equal(TrackEditing.MinKeyGap, TrackEditing.HoldSeconds(moved, 1), 1e-6f);
        Assert.Equal(new[] { 0f, 28.28f, 28.33f, 55.35f }, Times(moved));
    }

    [Fact]
    [Trait("Category", "Property")]
    public void DraggingAPointKeyKeepsEveryHold()
    {
        // A drag retimes legs and holds but never adds or removes one, so the key count stays too.
        Gen.Select(AnyPathTrack, Gen.Int[0, 5], Gen.Float[-10f, 1000f])
            .Select((track, point, time) => (Track: track, Point: point % track.Points.Count, Time: time))
            .Sample(
                x =>
                {
                    var moved = MoveKey(x.Track, TrackEditing.PointKey(x.Track, x.Point), x.Time);
                    Assert.Equal(
                        x.Track.Timing.Select(t => t.Hold > 0f).ToArray(),
                        moved.Timing.Select(t => t.Hold >= TrackEditing.MinKeyGap).ToArray()
                    );
                },
                iter: 2000,
                print: Kept<(Track Track, int Point, float Time)>(x =>
                    $"{PrintTrack(x.Track)}\nPoint {x.Point} dragged to {x.Time:R} s"
                )
            );
    }

    [Fact]
    public void RemovingAHold()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.HoldSeconds(TimingEditing.RemoveHold(held, 2), 1));
        Assert.Throws<ArgumentException>(() => TimingEditing.RemoveHold(held, 0));
    }

    [Fact]
    public void HandlesSetManualSidesAndBrokenIsKept()
    {
        var track = TimingEditing.SetHandles(Build3PointTrack(), 1, 0.1f, 0.3f);
        Assert.Equal(
            new PointTiming(InMode: TangentMode.Manual, OutMode: TangentMode.Manual, InTangent: 0.1f, OutTangent: 0.3f),
            track.Timing[1]
        );
        Assert.Equal(
            new TimingKey(5f, 1f, TangentMode.Manual, TangentMode.Manual, 0.1f, 0.3f),
            new TrackEvaluator(track).Keys[1] with
            {
                Time = 5f,
            }
        );

        track = TimingEditing.SetHandles(TimingEditing.SetBroken(track, 1, true), 1, null, 0.5f);
        Assert.Equal(
            (0.1f, 0.5f, true),
            (track.Timing[1].InTangent, track.Timing[1].OutTangent, track.Timing[1].Broken)
        );
    }

    [Fact]
    public void AHoldSplitsTheHandlesBetweenItsKeys()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var arrival = TimingEditing.SetHandles(held, 1, 0.1f, 0.3f);
        Assert.Equal(
            (TangentMode.Manual, TangentMode.Auto, 0.1f, 0f),
            (
                arrival.Timing[1].InMode,
                arrival.Timing[1].OutMode,
                arrival.Timing[1].InTangent,
                arrival.Timing[1].OutTangent
            )
        );

        var departure = TimingEditing.SetHandles(held, 2, 0.1f, 0.3f);
        Assert.Equal(
            (TangentMode.Auto, TangentMode.Manual, 0f, 0.3f),
            (
                departure.Timing[1].InMode,
                departure.Timing[1].OutMode,
                departure.Timing[1].InTangent,
                departure.Timing[1].OutTangent
            )
        );
    }

    [Fact]
    public void NegativeHandleSlopesClampToZero() =>
        Assert.Equal(0f, TimingEditing.SetHandles(Build3PointTrack(), 1, -2f, null).Timing[1].InTangent);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void AHandleSlopeThatIsNotFiniteLeavesItsSideAlone(float slope)
    {
        var track = Build3PointTrack();
        Assert.Same(track, TimingEditing.SetHandles(track, 1, slope, slope));

        // The finite out side still applies.
        var timing = TimingEditing.SetHandles(track, 1, slope, 0.5f).Timing[1];
        Assert.Equal(new PointTiming(OutMode: TangentMode.Manual, OutTangent: 0.5f), timing);
    }

    [Fact]
    public void HandlesExistOnlyWhereASpanIsNotAHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f); // keys 0, 5, 7, 12
        Assert.False(TimingEditing.HasHandle(track, 0, KeySide.In));
        Assert.True(TimingEditing.HasHandle(track, 0, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 1, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 2, KeySide.In));
        Assert.True(TimingEditing.HasHandle(track, 2, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 3, KeySide.Out));
    }

    [Fact]
    public void KeyEditsRejectAnOutOfRangeKey()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingEditing.SetKeyMode(track, 3, TangentMode.Flat));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingEditing.SetBroken(track, -1, true));
    }

    // Build3PointTrack is x = 0, 10, 20 at 2 yalms per second: two 10-yalm legs of 5 s, keys at
    // 0, 5 and 10. A leg of 10 yalms may last between 10 / MaxSpeed = 0.1 s and 10 / MinSpeed
    // clamped to MaxSeconds, so 0.1 s to 600 s — wide enough that nothing below clamps by accident.

    [Fact]
    public void RipplingAKeyCarriesTheLaterKeysAndChangesTheLength()
    {
        // Leg 1 becomes 7 s, leg 2 keeps its speed and so still takes 5 s: 7 + 5 = 12.
        var track = RippleKey(Build3PointTrack(), 1, 7f);

        Assert.Equal(new[] { 0f, 7f, 12f }, Times(track));
    }

    [Fact]
    public void RipplingDiffersFromTrimmingOnTheSameDrag()
    {
        // Trimming holds the last key at 10 by giving leg 2 the 3 s that leg 1 took.
        Assert.Equal(new[] { 0f, 7f, 10f }, Times(MoveKey(Build3PointTrack(), 1, 7f)));
        Assert.Equal(new[] { 0f, 7f, 12f }, Times(RippleKey(Build3PointTrack(), 1, 7f)));
    }

    [Fact]
    public void RipplingTheLastKeyOnlyChangesTheLength()
    {
        // Nothing follows key 2, so leg 2 stretches from 5 s to 7 s and key 1 stays at 5.
        var track = RippleKey(Build3PointTrack(), 2, 12f);

        Assert.Equal(new[] { 0f, 5f, 12f }, Times(track));
    }

    [Fact]
    public void RipplingClampsToTheLegsShortestDurationAndStillCarriesTheRest()
    {
        // 0.1 s is leg 1 at MaxSpeed; leg 2 is untouched, so key 2 lands at 0.1 + 5.
        var track = RippleKey(Build3PointTrack(), 1, -4f);

        Assert.Equal(new[] { 0f, 0.1f, 5.1f }, Times(track));
    }

    [Fact]
    public void RipplingRefusesTheFirstKeyAndANonFiniteTime()
    {
        var track = Build3PointTrack();

        Assert.Same(track, RippleKey(track, 0, 2f));
        Assert.Same(track, RippleKey(track, 1, float.NaN));
        Assert.Same(track, RippleKey(track, 1, float.PositiveInfinity));
        Assert.Same(track, RippleKey(track, 2, float.NegativeInfinity));
    }

    // Build3PointTrack with a 2 s hold on point 1 has keys 0 (point 0), 1 (point 1), 2 (its hold end) and 3 (point 2).
    // By HasHandle's rules a hold end has only an out side, a point key with a hold only an in side, and the last key no out side.

    [Fact]
    public void AHoldEndOffersRemovingItsHoldAndBreakingItsOutHandle()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);

        Assert.Equal(new KeyActions(true, true, false, KeySide.Out), TimingEditing.ActionsFor(track, 2));
        Assert.Equal(new KeyActions(false, true, false, KeySide.In), TimingEditing.ActionsFor(track, 1));
    }

    [Fact]
    public void ABrokenKeyOffersUnifyingFromItsOutSideWhenItHasOne()
    {
        var held = TimingEditing.SetBroken(TrackEditing.SetHold(Build3PointTrack(), 1, 2f), 1, true);
        var plain = TimingEditing.SetBroken(Build3PointTrack(), 0, true);

        Assert.Equal(new KeyActions(false, false, true, KeySide.In), TimingEditing.ActionsFor(held, 1));
        Assert.Equal(new KeyActions(true, false, true, KeySide.Out), TimingEditing.ActionsFor(held, 2));
        Assert.Equal(new KeyActions(false, false, true, KeySide.Out), TimingEditing.ActionsFor(plain, 0));
    }

    // Any is true when the menu offers even one thing: breaking alone, or unifying alone.

    [Fact]
    public void AKeyOfferingOneThingOffersSomething()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);

        Assert.True(TimingEditing.ActionsFor(track, 1).Any);
        Assert.True(TimingEditing.ActionsFor(TimingEditing.SetBroken(track, 1, true), 1).Any);
    }

    [Fact]
    public void TheOnlyKeyOfAOnePointTrackOffersNothing() =>
        Assert.False(TimingEditing.ActionsFor(TrackEditing.Append(TrackEditing.Empty(), Point(0f)), 0).Any);
}
