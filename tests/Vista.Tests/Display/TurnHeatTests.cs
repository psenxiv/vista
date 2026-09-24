using Vista.Core.Display;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Display;

public class TurnHeatTests
{
    // Recorded aim along x at 10 yalms a second, a point every second, looking level at the given yaws.
    private static TrackEvaluator Turning(params float[] yaws)
    {
        var track = TrackEditing.Empty() with { Speed = 10f };
        for (var i = 0; i < yaws.Length; i++)
            track = TrackEditing.Append(track, Point(i * 10f, yaw: yaws[i]));
        return new TrackEvaluator(track);
    }

    [Fact]
    public void AnEvenTurnReadsAsItsAngularSpeed()
    {
        // Yaw 0, 1, 2, 3 a second apart: the middle leg's slopes are both 1 per second, so it turns at exactly 1 rad/s ≈ 57.296°/s.
        var samples = TurnHeat.Samples(Turning(0f, 1f, 2f, 3f));

        foreach (var sample in samples.Skip(32).Take(27))
            Assert.Equal(57.296f, sample.DegreesPerSecond, 0.05f);
    }

    [Fact]
    public void AStillLookReadsAsZero() =>
        Assert.All(TurnHeat.Samples(Turning(0.5f, 0.5f, 0.5f)), s => Assert.Equal(0f, s.DegreesPerSecond, 1e-3f));

    [Fact]
    public void ItSamplesThirtyTimesASecondAndTheFirstHasNoRate()
    {
        // A 3 s track: 91 samples, 0 to 3 s.
        var samples = TurnHeat.Samples(Turning(0f, 1f, 2f, 3f));

        Assert.Equal(91, samples.Count);
        Assert.Equal(0f, samples[0].DegreesPerSecond);
        Assert.Equal(30f, samples[^1].Position.X, 1e-3f);
    }

    [Fact]
    public void AStraightPathReadsAsZero()
    {
        var track = TrackEditing.Empty(AimMode.PathTangent) with { Speed = 10f };
        foreach (var x in new[] { 0f, 10f, 20f })
            track = TrackEditing.Append(track, Point(x));

        Assert.All(TurnHeat.Samples(new TrackEvaluator(track)), s => Assert.Equal(0f, s.DegreesPerSecond, 1e-2f));
    }

    [Fact]
    public void ATrackWithNoPointsHasNoSamples() =>
        Assert.Empty(TurnHeat.Samples(new TrackEvaluator(TrackEditing.Empty())));

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, TurnHeat.Warm)]
    [InlineData(90f, 1f)]
    [InlineData(180f, 1f)]
    [InlineData(-5f, 0f)]
    public void TheScaleIsWarmAt45AndHotAt90(float degreesPerSecond, float level) =>
        Assert.Equal(level, TurnHeat.Level(degreesPerSecond), 1e-5f);
}
