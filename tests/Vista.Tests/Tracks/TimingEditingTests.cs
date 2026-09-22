using System;
using System.Linq;
using System.Numerics;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class TimingEditingTests
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

    private static Track WithInner(Track track, float time, float position)
    {
        var timing = track.Timing.Append(new TimingKey(time, position)).OrderBy(k => k.Time).ToList();
        return track with { Timing = timing };
    }

    [Fact]
    public void SetKeyModeSetsBothSidesAndRefusesManual()
    {
        var track = TimingEditing.SetKeyMode(Build3PointTrack(), 1, TangentMode.Linear);
        Assert.Equal((TangentMode.Linear, TangentMode.Linear), (track.Timing[1].InMode, track.Timing[1].OutMode));
        Assert.Throws<ArgumentException>(() => TimingEditing.SetKeyMode(track, 1, TangentMode.Manual));
    }

    [Fact]
    public void MovingAPointKeySqueezesItsNeighboursAndKeepsTheLength()
    {
        var track = TimingEditing.MoveKey(Build3PointTrack(), 1, 3f, 0f);
        Assert.Equal(new[] { 0f, 3f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(1f, track.Timing[1].Position);
    }

    [Fact]
    public void MovingAPointKeyRescalesTheInnerKeysOfBothLegs()
    {
        var track = WithInner(WithInner(Build3PointTrack(), 2f, 0.5f), 7.5f, 1.5f);   // keys 0, 2, 5, 7.5, 10
        track = TimingEditing.MoveKey(track, 2, 4f, 0f);
        Assert.Equal(new[] { 0f, 1.6f, 4f, 7f, 10f }, track.Timing.Select(k => k.Time).Select(t => MathF.Round(t, 3)));
    }

    [Fact]
    public void APointKeyDragClampsToTheShortestLegs()
    {
        var track = TimingEditing.MoveKey(Build3PointTrack(), 1, -4f, 0f);
        Assert.Equal(TrackEditing.MinLegSeconds, track.Timing[1].Time, 4);
        track = TimingEditing.MoveKey(Build3PointTrack(), 1, 50f, 0f);
        Assert.Equal(10f - TrackEditing.MinLegSeconds, track.Timing[1].Time, 4);
    }

    [Fact]
    public void TheFirstKeyNeverMovesAndTheLastKeyChangesTheLength()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TimingEditing.MoveKey(track, 0, 2f, 0f));
        Assert.Equal(12f, TimingEditing.MoveKey(track, 2, 12f, 0f).Timing[2].Time);
    }

    [Fact]
    public void AHoldEndDragChangesTheHoldAndTheNextLeg()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys 0, 5, 7, 12
        track = TimingEditing.MoveKey(track, 2, 8f, 99f);
        Assert.Equal(new[] { 0f, 5f, 8f, 12f }, track.Timing.Select(k => k.Time));
        Assert.Equal(1f, track.Timing[2].Position);
    }

    [Fact]
    public void AnInnerKeyMovesInTimeAndPlaceBetweenItsNeighbours()
    {
        var track = WithInner(Build3PointTrack(), 2f, 0.5f);
        var moved = TimingEditing.MoveKey(track, 1, 3f, 0.7f);
        Assert.Equal((3f, 0.7f), (moved.Timing[1].Time, moved.Timing[1].Position));

        var clamped = TimingEditing.MoveKey(track, 1, 9f, 1.5f);
        Assert.Equal(5f - TrackEditing.MinKeyGap, clamped.Timing[1].Time, 4);
        Assert.True(clamped.Timing[1].Position < 1f);
    }

    [Fact]
    public void AnInnerKeyWithNeighboursOneTenthApartDragsWithoutThrowing()
    {
        // At 0.3 and 0.4 s, float rounding puts prev + gap a step above next - gap.
        var track = WithInner(WithInner(WithInner(Build3PointTrack(), 0.3f, 0.06f), 0.4f, 0.08f), 0.35f, 0.07f);
        Assert.True(0.3f + TrackEditing.MinKeyGap > 0.4f - TrackEditing.MinKeyGap);

        var moved = TimingEditing.MoveKey(track, 2, 3f, 0.07f);

        Assert.InRange(moved.Timing[2].Time, 0.3f, 0.4f);
    }

    [Fact]
    public void MovingAnInnerKeyToItsCurrentPlaceReturnsTheSameInstance()
    {
        var track = WithInner(Build3PointTrack(), 2f, 0.5f);
        Assert.Same(track, TimingEditing.MoveKey(track, 1, 2f, 0.5f));
    }

    [Fact]
    public void AnInnerKeyIsAddedWithUnbrokenManualHandles()
    {
        var (track, key) = TimingEditing.AddInnerKey(Build3PointTrack(), 2f, 0.4f, 0.2f);
        Assert.Equal(1, key);
        Assert.Equal(new TimingKey(2f, 0.4f, TangentMode.Manual, TangentMode.Manual, 0.2f, 0.2f), track.Timing[1]);
    }

    [Fact]
    public void AnInnerKeyCannotGoInAHoldOrTooCloseToAnotherKey()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(held, 6f, 1f, 0f));
        Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(Build3PointTrack(), 4.98f, 0.99f, 0f));
    }

    [Fact]
    public void AnInnerKeyRefusesAtTheLastKeysTime()
        => Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(Build3PointTrack(), 10f, 2f, 0f));

    [Fact]
    public void AnInnerKeyRefusesAtAnInteriorPointsTime()
        => Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(Build3PointTrack(), 5f, 1f, 0f));

    [Fact]
    public void DeletingKeys()
    {
        var inner = WithInner(Build3PointTrack(), 2f, 0.5f);
        Assert.Equal(3, TimingEditing.DeleteKey(inner, 1).Timing.Count);

        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.HoldSeconds(TimingEditing.DeleteKey(held, 2), 1));

        Assert.Throws<ArgumentException>(() => TimingEditing.DeleteKey(inner, 0));
    }

    [Fact]
    public void HandlesSetManualSidesAndBrokenIsKept()
    {
        var track = TimingEditing.SetHandles(Build3PointTrack(), 1, 0.1f, 0.3f);
        Assert.Equal(new TimingKey(5f, 1f, TangentMode.Manual, TangentMode.Manual, 0.1f, 0.3f), track.Timing[1]);

        track = TimingEditing.SetHandles(TimingEditing.SetBroken(track, 1, true), 1, null, 0.5f);
        Assert.Equal((0.1f, 0.5f, true), (track.Timing[1].InTangent, track.Timing[1].OutTangent, track.Timing[1].Broken));
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
}
