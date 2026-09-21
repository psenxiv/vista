using System.Linq;
using System.Numerics;
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

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
        Assert.Equal(TangentMode.Auto, track.Timing[0].Mode);
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
            Assert.Equal(TangentMode.Auto, key.Mode);
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
}
