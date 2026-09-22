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

    // Measured in game: yaw 0 looks along -Z and yaw pi/2 along -X, so the view direction is
    // (-sin yaw cos pitch, sin pitch, -cos yaw cos pitch) and the strafe axis is level at
    // (cos yaw, 0, -sin yaw). At yaw 45 and pitch 30 degrees, one second at speed 1 moves
    // (-0.6123724, 0.5, -0.6123724) forward and (0.7071068, 0, -0.7071068) right.
    [Theory]
    [InlineData(1f, 0f, 0f, -0.6123724f, 0.5f, -0.6123724f)]
    [InlineData(-1f, 0f, 0f, 0.6123724f, -0.5f, 0.6123724f)]
    [InlineData(0f, 0f, 1f, 0.7071068f, 0f, -0.7071068f)]
    [InlineData(0f, 0f, -1f, -0.7071068f, 0f, 0.7071068f)]
    [InlineData(0f, 1f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, -1f, 0f, 0f, -1f, 0f)]
    public void OneSecondOfInputMovesAlongTheHandComputedAxes(float forward, float up, float right, float x, float y, float z)
    {
        var moved = FreeCamMotion.Step(Vector3.Zero, new Vector3(forward, up, right), MathF.PI / 4f, MathF.PI / 6f, 1f, 1f);

        Assert.Equal(x, moved.X, 5);
        Assert.Equal(y, moved.Y, 5);
        Assert.Equal(z, moved.Z, 5);
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
        // Measured in game: yaw 0 looks along -Z, yaw pi/2 looks along -X. Ten units ahead at
        // yaw 45 and pitch 30 degrees is therefore (-6.1237244, 5, -6.1237244).
        var ahead = FreeCamMotion.LookAtFrom(Vector3.Zero, MathF.PI / 4f, MathF.PI / 6f);

        Assert.Equal(-6.1237244f, ahead.X, 4);
        Assert.Equal(5f, ahead.Y, 4);
        Assert.Equal(-6.1237244f, ahead.Z, 4);
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
