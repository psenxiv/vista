using System.Numerics;
using Vista.Core.Camera;
using Xunit;

namespace Vista.Tests.Camera;

public class FreeCamMotionTests
{
    [Fact]
    public void NoInputDoesNotMove()
    {
        var start = new Vector3(10, 20, 30);
        Assert.Equal(start, FreeCamMotion.Step(start, Vector3.Zero, 0f, 0f, 5f, 0.016f));
    }

    [Fact]
    public void MotionIsFramerateIndependent()
    {
        var input = new Vector3(1, 0, 0);
        var oneBigStep = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 5f, 0.1f);

        var accumulated = Vector3.Zero;
        for (var i = 0; i < 10; i++)
            accumulated = FreeCamMotion.Step(accumulated, input, 0f, 0f, 5f, 0.01f);

        Assert.True(Vector3.Distance(oneBigStep, accumulated) < 0.0001f,
            $"expected {oneBigStep}, accumulated {accumulated}");
    }

    [Fact]
    public void SpeedScalesDistanceLinearly()
    {
        var input = new Vector3(1, 0, 0);
        // One second of full forward input at speed 1 and speed 2 travels exactly 1 and 2 units.
        var slow = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 1f, 1f);
        var fast = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 2f, 1f);

        Assert.Equal(1f, slow.Length(), 5);
        Assert.Equal(2f, fast.Length(), 5);
    }

    [Fact]
    public void UpInputMovesOnYOnly()
    {
        // Up input is world up, untouched by yaw or pitch: one second at speed 3 rises exactly 3.
        var result = FreeCamMotion.Step(Vector3.Zero, new Vector3(0, 1, 0), 1.2f, 0.4f, 3f, 1f);

        Assert.Equal(0f, result.X, 4);
        Assert.Equal(0f, result.Z, 4);
        Assert.Equal(3f, result.Y, 4);
    }

    [Fact]
    public void ForwardInputMovesAlongTheViewDirection()
    {
        const float yaw = 1.2f;
        const float pitch = -0.3f;

        var moved = FreeCamMotion.Step(Vector3.Zero, new Vector3(1, 0, 0), yaw, pitch, 5f, 1f);
        var lookAt = FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch);

        var movedDir = Vector3.Normalize(moved);
        var lookDir = Vector3.Normalize(lookAt);

        Assert.Equal(1f, Vector3.Dot(movedDir, lookDir), 4);
    }

    [Fact]
    public void StrafeIsPerpendicularToTheViewDirection()
    {
        const float yaw = 1.2f;
        const float pitch = -0.3f;

        var strafed = FreeCamMotion.Step(Vector3.Zero, new Vector3(0, 0, 1), yaw, pitch, 5f, 1f);
        var lookDir = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));

        Assert.Equal(0f, Vector3.Dot(Vector3.Normalize(strafed), lookDir), 4);
        Assert.Equal(0f, strafed.Y, 4);
    }

    [Fact]
    public void LookAtSitsTenUnitsAheadOfPosition()
    {
        var position = new Vector3(5, 5, 5);
        Assert.Equal(10f, Vector3.Distance(position, FreeCamMotion.LookAtFrom(position, 0f, 0f)), 3);
    }

    [Fact]
    public void LookAtUsesTheGameDirectionConvention()
    {
        // Measured in game: yaw 0 looks along -Z, yaw pi/2 looks along -X.
        Assert.True(FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0f).Z < 0);
        Assert.True(FreeCamMotion.LookAtFrom(Vector3.Zero, MathF.PI / 2f, 0f).X < 0);
    }

    [Fact]
    public void PitchUpRaisesTheLookAtTarget()
    {
        var level = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0f);
        var raised = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0.5f);
        Assert.True(raised.Y > level.Y);
    }

    [Fact]
    public void RollLookChangesNothingUnrolled()
    {
        var (yaw, pitch) = FreeCamMotion.RollLook(0.3f, -0.2f, 0f);
        Assert.Equal(0.3f, yaw, 6);
        Assert.Equal(-0.2f, pitch, 6);
    }

    [Fact]
    public void RolledRightAQuarterTurnADragRightLooksDown()
    {
        // Unrolled, a right turn lowers yaw (FreeCamMotion's convention); rolled right 90 degrees, screen right is world down.
        var (yaw, pitch) = FreeCamMotion.RollLook(-0.1f, 0f, MathF.PI / 2f);
        Assert.Equal(0f, yaw, 5);
        Assert.Equal(-0.1f, pitch, 5);
    }

    [Fact]
    public void RolledRightAQuarterTurnADragUpTurnsRight()
    {
        var (yaw, pitch) = FreeCamMotion.RollLook(0f, 0.1f, MathF.PI / 2f);
        Assert.Equal(-0.1f, yaw, 5);
        Assert.Equal(0f, pitch, 5);
    }

    [Fact]
    public void UpsideDownADragRightTurnsLeft()
    {
        var (yaw, pitch) = FreeCamMotion.RollLook(-0.1f, 0f, MathF.PI);
        Assert.Equal(0.1f, yaw, 5);
        Assert.Equal(0f, pitch, 5);
    }
}
