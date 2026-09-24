using System.Linq;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks;

public class LegEasingTests
{
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
    public void SettingEasingZeroesTheSidesTangents()
    {
        var track = TimingEditing.SetHandles(TimingEditing.SetHandles(Build3PointTrack(), 0, null, 0.3f), 1, 0.2f, null);
        track = LegEasing.Set(track, 1, Easing.Linear);
        Assert.Equal((0f, 0f), (track.Timing[0].OutTangent, track.Timing[1].InTangent));
    }

    [Fact]
    public void CustomCannotBeSet() => Assert.Throws<ArgumentOutOfRangeException>(() => LegEasing.Set(Build3PointTrack(), 1, Easing.Custom));

    [Fact]
    public void EasingNeverChangesTimes()
    {
        var track = LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut);
        Assert.Equal(new[] { 0f, 5f, 10f }, new TrackEvaluator(track).Keys.Select(k => MathF.Round(k.Time, 2)));
    }
}
