using CinematicCam.Core.Camera;
using Xunit;

namespace CinematicCam.Tests.Camera;

public class FlySpeedTests
{
    [Fact]
    public void StartsAtNormalSpeed()
    {
        Assert.Equal(1f, new FlySpeed().Multiplier);
    }

    [Fact]
    public void StepsThroughTheFixedMultipliers()
    {
        var speed = new FlySpeed();

        speed.Step(1);
        Assert.Equal(2f, speed.Multiplier);
        speed.Step(1);
        Assert.Equal(4f, speed.Multiplier);
        speed.Step(-3);
        Assert.Equal(0.5f, speed.Multiplier);
        speed.Step(-1);
        Assert.Equal(0.25f, speed.Multiplier);
    }

    [Fact]
    public void StopsAtEitherEnd()
    {
        var speed = new FlySpeed();

        speed.Step(10);
        Assert.Equal(4f, speed.Multiplier);
        speed.Step(-10);
        Assert.Equal(0.25f, speed.Multiplier);
    }

    [Fact]
    public void SetClampsToTheSteps()
    {
        var speed = new FlySpeed();

        speed.Set(4);
        Assert.Equal(4f, speed.Multiplier);
        speed.Set(99);
        Assert.Equal(4, speed.Index);
        speed.Set(-1);
        Assert.Equal(0, speed.Index);
    }
}
