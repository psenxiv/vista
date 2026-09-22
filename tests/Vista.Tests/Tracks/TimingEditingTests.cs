using System;
using System.Linq;
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class TimingEditingTests
{
    private static ControlPoint Point(float x, float y, float z)
        => new(new Vector3(x, y, z), 0f, 0f, 1f);

    // Points at 0,10,20 at 2 yalms per second: keys at times 0, 5, 10.
    private static Track Build3PointTrack()
    {
        var track = TrackEditing.Empty() with { Speed = 2f };
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        track = TrackEditing.Append(track, Point(20f, 0f, 0f));
        return track;
    }

    private static Track MoveKey(Track track, int key, float time) => TimingEditing.MoveKey(track, new TrackEvaluator(track), key, time);

    private static float[] Times(Track track) => new TrackEvaluator(track).Keys.Select(k => MathF.Round(k.Time, 2)).ToArray();

    [Fact]
    public void SetKeyModeSetsBothSidesAndRefusesManual()
    {
        var track = TimingEditing.SetKeyMode(Build3PointTrack(), 1, TangentMode.Linear);
        Assert.Equal((TangentMode.Linear, TangentMode.Linear), (track.Timing[1].InMode, track.Timing[1].OutMode));
        Assert.Throws<ArgumentException>(() => TimingEditing.SetKeyMode(track, 1, TangentMode.Manual));
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
        Assert.Equal(12f, new TrackEvaluator(MoveKey(track, 2, 12f)).Keys[2].Time, 2);
    }

    [Fact]
    public void AHoldEndDragChangesTheHoldAndShiftsLaterKeys()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys 0, 5, 7, 12
        track = MoveKey(track, 2, 8f);
        Assert.Equal(new[] { 0f, 5f, 8f, 13f }, Times(track));
        Assert.Equal(1f, new TrackEvaluator(track).Keys[2].Position);
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
        Assert.Equal(new PointTiming(InMode: TangentMode.Manual, OutMode: TangentMode.Manual, InTangent: 0.1f, OutTangent: 0.3f), track.Timing[1]);
        Assert.Equal(new TimingKey(5f, 1f, TangentMode.Manual, TangentMode.Manual, 0.1f, 0.3f), new TrackEvaluator(track).Keys[1] with { Time = 5f });

        track = TimingEditing.SetHandles(TimingEditing.SetBroken(track, 1, true), 1, null, 0.5f);
        Assert.Equal((0.1f, 0.5f, true), (track.Timing[1].InTangent, track.Timing[1].OutTangent, track.Timing[1].Broken));
    }

    [Fact]
    public void AHoldSplitsTheHandlesBetweenItsKeys()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var arrival = TimingEditing.SetHandles(held, 1, 0.1f, 0.3f);
        Assert.Equal((TangentMode.Manual, TangentMode.Auto, 0.1f, 0f), (arrival.Timing[1].InMode, arrival.Timing[1].OutMode, arrival.Timing[1].InTangent, arrival.Timing[1].OutTangent));

        var departure = TimingEditing.SetHandles(held, 2, 0.1f, 0.3f);
        Assert.Equal((TangentMode.Auto, TangentMode.Manual, 0f, 0.3f), (departure.Timing[1].InMode, departure.Timing[1].OutMode, departure.Timing[1].InTangent, departure.Timing[1].OutTangent));
    }

    [Fact]
    public void NegativeHandleSlopesClampToZero()
        => Assert.Equal(0f, TimingEditing.SetHandles(Build3PointTrack(), 1, -2f, null).Timing[1].InTangent);

    [Fact]
    public void HandlesExistOnlyWhereASpanIsNotAHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys 0, 5, 7, 12
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
}
