using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AnchorTests
{
    private const float Tolerance = 1e-4f;

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void TheOriginChangesNothing()
    {
        var point = new ControlPoint(new Vector3(3f, 4f, 5f), 0.5f, 0.2f, 1f, 0.1f);
        Assert.Equal(point, Anchor.Origin.ToWorld(point));
        Assert.Equal(point, Anchor.Origin.ToLocal(point));
    }

    [Fact]
    public void AQuarterTurnTurnsTheForwardOffsetTheWayTheLookTurns()
    {
        var anchor = new Anchor(Vector3.Zero, MathF.PI / 2f);
        Near(new Vector3(-1f, 0f, 0f), anchor.ToWorld(new Vector3(0f, 0f, -1f)));
    }

    [Theory]
    [InlineData(0.3f, 0.1f, 0.7f)]
    [InlineData(-2.5f, -0.4f, 1.9f)]
    [InlineData(3.0f, 0.6f, -3.0f)]
    public void TurningAPointAgreesWithAddingToItsYaw(float yaw, float pitch, float turn)
    {
        var look = FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch);
        Near(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw + turn, pitch), Anchor.Turn(look, turn));
    }

    [Fact]
    public void ToLocalUndoesToWorldForPositions()
    {
        var anchor = new Anchor(new Vector3(10f, -2f, 7f), 1.1f);
        var local = new Vector3(3f, 1.5f, -4f);

        Near(local, anchor.ToLocal(anchor.ToWorld(local)));
        Near(new Vector3(10f, -0.5f, 7f) + Anchor.Turn(new Vector3(3f, 0f, -4f), 1.1f), anchor.ToWorld(local));
    }

    [Fact]
    public void APointGainsTheAnchorsYawAndKeepsItsPitchRollAndFov()
    {
        var anchor = new Anchor(new Vector3(1f, 2f, 3f), 0.8f);
        var local = new ControlPoint(new Vector3(0f, 0f, -2f), 0.2f, 0.3f, 1.1f, 0.4f);
        var world = anchor.ToWorld(local);

        Assert.Equal(1.0f, world.Yaw, Tolerance);
        Assert.Equal(0.3f, world.Pitch);
        Assert.Equal(0.4f, world.Roll);
        Assert.Equal(1.1f, world.Fov);
        Near(local.Position, anchor.ToLocal(world).Position);
        Assert.Equal(local.Yaw, anchor.ToLocal(world).Yaw, Tolerance);
    }

    [Fact]
    public void NestedAnchorsComposeAndUndo()
    {
        var scene = new Anchor(new Vector3(100f, 5f, -50f), 0.6f);
        var track = new Anchor(new Vector3(4f, 0f, 2f), -1.3f);
        var point = new Vector3(1f, 2f, 3f);

        var composed = scene.ToWorld(track);
        Near(scene.ToWorld(track.ToWorld(point)), composed.ToWorld(point));
        Assert.Equal(0.6f - 1.3f, composed.Yaw, Tolerance);

        var back = scene.ToLocal(composed);
        Near(track.Position, back.Position);
        Assert.Equal(track.Yaw, back.Yaw, Tolerance);
    }
}
