using System.Linq;
using System.Numerics;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Tracks;

public class LegEasingTests
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

    [Theory]
    [InlineData(Easing.Smooth, TangentMode.Auto, TangentMode.Auto)]
    [InlineData(Easing.Linear, TangentMode.Linear, TangentMode.Linear)]
    [InlineData(Easing.EaseIn, TangentMode.Flat, TangentMode.Auto)]
    [InlineData(Easing.EaseOut, TangentMode.Auto, TangentMode.Flat)]
    [InlineData(Easing.EaseInOut, TangentMode.Flat, TangentMode.Flat)]
    public void APresetSetsTheSidesBoundingItsLegAndReadsBack(Easing easing, TangentMode outMode, TangentMode inMode)
    {
        var track = LegEasing.Set(Build3PointTrack(), 2, easing);
        Assert.Equal(outMode, track.Timing[1].OutMode);
        Assert.Equal(inMode, track.Timing[2].InMode);
        Assert.Equal(easing, LegEasing.Read(track, 2));
        Assert.Equal(Easing.Smooth, LegEasing.Read(track, 1));
    }

    [Fact]
    public void NewLegsAreSmooth() => Assert.Equal(Easing.Smooth, LegEasing.Read(Build3PointTrack(), 1));

    [Fact]
    public void AManualSideReadsCustom()
    {
        var track = Build3PointTrack();
        var timing = track.Timing.ToList();
        timing[1] = timing[1] with { OutMode = TangentMode.Manual, OutTangent = 0.1f };
        Assert.Equal(Easing.Custom, LegEasing.Read(track with { Timing = timing }, 2));
    }

    [Fact]
    public void SettingTheSameEasingReturnsTheSameTrack()
    {
        var track = Build3PointTrack();
        Assert.Same(track, LegEasing.Set(track, 1, Easing.Smooth));
    }

    [Fact]
    public void CustomCannotBeSet() => Assert.Throws<ArgumentOutOfRangeException>(() => LegEasing.Set(Build3PointTrack(), 1, Easing.Custom));

    [Fact]
    public void EasingNeverChangesTimes()
    {
        var track = LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut);
        Assert.Equal(new[] { 0f, 5f, 10f }, track.Timing.Select(k => k.Time));
    }
}
