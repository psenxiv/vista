using System.Numerics;
using Vista.Core.Tracks.Aiming;
using Xunit;

namespace Vista.Tests.Tracks.Aiming;

public class AimSmootherTests
{
    private static readonly Vector3 Start = Vector3.Zero;
    private static readonly Vector3 Target = new(10f, 0f, 0f);

    // At smoothing 1 the time constant is 0.5 s; half a second closes 1 − 1/e of the gap.
    private static readonly float HalfSecondAtFull = 10f * (1f - MathF.Exp(-1f));

    [Fact]
    public void TheFirstStepLandsOnTheTarget()
        => Assert.Equal(Target, new AimSmoother().Step(Target, 1f / 60f, 1f));

    [Fact]
    public void AtFullSmoothingHalfASecondClosesOneTimeConstant()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 1f);

        Assert.Equal(HalfSecondAtFull, smoother.Step(Target, 0.5f, 1f).X, 4);
    }

    [Fact]
    public void EasingDependsOnTimeNotOnFrames()
    {
        var sixty = new AimSmoother();
        var thirty = new AimSmoother();
        sixty.Step(Start, 0f, 0.3f);
        thirty.Step(Start, 0f, 0.3f);
        var a = Vector3.Zero;
        var b = Vector3.Zero;

        for (var i = 0; i < 12; i++) a = sixty.Step(Target, 1f / 60f, 0.3f);
        for (var i = 0; i < 6; i++) b = thirty.Step(Target, 1f / 30f, 0.3f);

        Assert.Equal(a.X, b.X, 3);
    }

    [Fact]
    public void ZeroSmoothingIsExact()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 0f);

        Assert.Equal(Target, smoother.Step(Target, 1f / 60f, 0f));
    }

    [Fact]
    public void AResetStartsAfreshWithNoEasing()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 1f);
        smoother.Reset();

        Assert.Equal(Target, smoother.Step(Target, 1f / 60f, 1f));
    }

    [Fact]
    public void NoTimeHoldsWhereItIs()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 0f);

        Assert.Equal(Start, smoother.Step(Target, 0f, 0f));
    }

    [Fact]
    public void ASeedIsWhereEasingStartsFrom()
    {
        var smoother = new AimSmoother();
        smoother.Seed(Start);

        Assert.Equal(HalfSecondAtFull, smoother.Step(Target, 0.5f, 1f).X, 4);
    }
}
