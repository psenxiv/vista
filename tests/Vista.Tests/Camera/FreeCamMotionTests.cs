using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Camera;

public class FreeCamMotionTests
{
    // Measured in game: yaw 0 looks along -Z and yaw pi/2 along -X. At yaw 45 and pitch 30 degrees the facing is
    // (-sin45 cos30, sin30, -cos45 cos30) = (-0.6123724, 0.5, -0.6123724), the upright up its pitch-derivative
    // (sin45 sin30, cos30, cos45 sin30) = (0.3535534, 0.8660254, 0.3535534), and the right their cross product
    // (0.7071068, 0, -0.7071068).
    private static readonly Quaternion Yaw45Pitch30 = CameraRotation.FromAngles(45f * Deg, 30f * Deg, 0f);

    [Fact]
    public void NoInputDoesNotMove()
    {
        var start = new Vector3(10, 20, 30);
        Assert.Equal(start, FreeCamMotion.Step(start, Vector3.Zero, Yaw45Pitch30, 5f, 0.016f));
    }

    [Fact]
    public void MotionIsFramerateIndependent()
    {
        var input = new Vector3(1, 0, 0);
        var oneBigStep = FreeCamMotion.Step(Vector3.Zero, input, Quaternion.Identity, 5f, 0.1f);

        var accumulated = Vector3.Zero;
        for (var i = 0; i < 10; i++)
            accumulated = FreeCamMotion.Step(accumulated, input, Quaternion.Identity, 5f, 0.01f);

        Assert.True(
            Vector3.Distance(oneBigStep, accumulated) < 0.0001f,
            $"expected {oneBigStep}, accumulated {accumulated}"
        );
    }

    [Fact]
    public void SpeedScalesDistanceLinearly()
    {
        var input = new Vector3(1, 0, 0);
        // One second of full forward input at speed 1 and speed 2 travels exactly 1 and 2 units.
        var slow = FreeCamMotion.Step(Vector3.Zero, input, Yaw45Pitch30, 1f, 1f);
        var fast = FreeCamMotion.Step(Vector3.Zero, input, Yaw45Pitch30, 2f, 1f);

        Assert.Equal(1f, slow.Length(), 5);
        Assert.Equal(2f, fast.Length(), 5);
    }

    // One second at speed 1 moves one unit along the facing or the camera's right, as derived above, or straight up or down.
    [Theory]
    [InlineData(1f, 0f, 0f, -0.6123724f, 0.5f, -0.6123724f)]
    [InlineData(-1f, 0f, 0f, 0.6123724f, -0.5f, 0.6123724f)]
    [InlineData(0f, 0f, 1f, 0.7071068f, 0f, -0.7071068f)]
    [InlineData(0f, 0f, -1f, -0.7071068f, 0f, 0.7071068f)]
    [InlineData(0f, 1f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, -1f, 0f, 0f, -1f, 0f)]
    public void OneSecondOfInputMovesAlongTheViewOrStraightUp(
        float forward,
        float up,
        float right,
        float x,
        float y,
        float z
    )
    {
        var moved = FreeCamMotion.Step(Vector3.Zero, new Vector3(forward, up, right), Yaw45Pitch30, 1f, 1f);

        Assert.Equal(x, moved.X, 5);
        Assert.Equal(y, moved.Y, 5);
        Assert.Equal(z, moved.Z, 5);
    }

    [Fact]
    public void UpInputUpsideDownStillMovesStraightUp()
    {
        // Rolled half a turn about the facing (0,0,-1), the camera's up is world (0,-1,0), but up input moves straight up:
        // one second at speed 3 rises 3.
        var moved = FreeCamMotion.Step(
            Vector3.Zero,
            new Vector3(0, 1, 0),
            CameraRotation.FromAngles(0f, 0f, MathF.PI),
            3f,
            1f
        );

        Assert.Equal(0f, moved.X, 5);
        Assert.Equal(3f, moved.Y, 5);
        Assert.Equal(0f, moved.Z, 5);
    }

    [Fact]
    public void PitchingPastVerticalTurnsTheCameraOver()
    {
        // Pitching 100 degrees about the right (1,0,0) takes forward (0,0,-1) to (0, sin100, -cos100) = (0, 0.9848078, 0.1736482)
        // and up (0,1,0) to (0, cos100, sin100) = (0, -0.1736482, 0.9848078): past vertical, the picture is upside down.
        var rotation = FreeCamMotion.Turn(Quaternion.Identity, 0f, 100f * Deg);

        var forward = CameraRotation.Forward(rotation);
        var up = CameraRotation.Up(rotation);
        Assert.Equal(0f, forward.X, 5);
        Assert.Equal(0.9848078f, forward.Y, 5);
        Assert.Equal(0.1736482f, forward.Z, 5);
        Assert.Equal(0f, up.X, 5);
        Assert.Equal(-0.1736482f, up.Y, 5);
        Assert.Equal(0.9848078f, up.Z, 5);
    }

    [Fact]
    public void YawingUnrolledMatchesTheGamesYaw()
    {
        // Yaw grows toward -X (measured in game), so 90 degrees from identity faces (-1,0,0) with the up unchanged.
        var rotation = FreeCamMotion.Turn(Quaternion.Identity, 90f * Deg, 0f);

        var forward = CameraRotation.Forward(rotation);
        var up = CameraRotation.Up(rotation);
        Assert.Equal(-1f, forward.X, 5);
        Assert.Equal(0f, forward.Y, 5);
        Assert.Equal(0f, forward.Z, 5);
        Assert.Equal(0f, up.X, 5);
        Assert.Equal(1f, up.Y, 5);
        Assert.Equal(0f, up.Z, 5);
    }

    [Fact]
    public void YawingWhilePitchedKeepsTheHorizonLevel()
    {
        // Pitching 45 degrees down faces (0, -√½, -√½) with up (0, √½, -√½); yawing 90 degrees about the vertical, toward
        // -X, turns both about it: facing (-√½, -√½, 0) and up (-√½, √½, 0), still square to the ground's level.
        var rotation = FreeCamMotion.Turn(FreeCamMotion.Turn(Quaternion.Identity, 0f, -45f * Deg), 90f * Deg, 0f);

        var half = MathF.Sqrt(0.5f);
        Near(new Vector3(-half, -half, 0f), CameraRotation.Forward(rotation), 1e-5f);
        Near(new Vector3(-half, half, 0f), CameraRotation.Up(rotation), 1e-5f);
    }

    [Fact]
    public void LookingAroundInACircleComesBackLevel()
    {
        // Down 45, left 90, up 45, right 90: the turns about the vertical undo each other, as do the pitches between them,
        // so the camera faces (0, 0, -1) with up (0, 1, 0) again, with no roll picked up on the way.
        var rotation = Quaternion.Identity;
        foreach (var (yaw, pitch) in new[] { (0f, -45f), (90f, 0f), (0f, 45f), (-90f, 0f) })
            rotation = FreeCamMotion.Turn(rotation, yaw * Deg, pitch * Deg);

        Near(new Vector3(0f, 0f, -1f), CameraRotation.Forward(rotation), 1e-5f);
        Near(Vector3.UnitY, CameraRotation.Up(rotation), 1e-5f);
    }

    [Fact]
    public void YawingUpsideDownFollowsThePicture()
    {
        // Pitched half a turn, the camera faces (0,0,1) with up (0,-1,0). Yawing 90 degrees turns about the vertical the
        // other way, so it faces (-1,0,0), the picture's left as when upright; the plain way round it would face (1,0,0).
        var inverted = FreeCamMotion.Turn(Quaternion.Identity, 0f, MathF.PI);
        var rotation = FreeCamMotion.Turn(inverted, 90f * Deg, 0f);

        var forward = CameraRotation.Forward(rotation);
        var up = CameraRotation.Up(rotation);
        Assert.Equal(-1f, forward.X, 5);
        Assert.Equal(0f, forward.Y, 5);
        Assert.Equal(0f, forward.Z, 5);
        Assert.Equal(0f, up.X, 5);
        Assert.Equal(-1f, up.Y, 5);
        Assert.Equal(0f, up.Z, 5);
    }

    [Fact]
    public void RollingRightTipsTheUpToTheRight()
    {
        // Rolling 90 degrees about the facing (0,0,-1) keeps the facing and takes up (0,1,0) to the right, (1,0,0).
        var rotation = FreeCamMotion.Roll(Quaternion.Identity, 90f * Deg);

        var forward = CameraRotation.Forward(rotation);
        var up = CameraRotation.Up(rotation);
        Assert.Equal(0f, forward.X, 5);
        Assert.Equal(0f, forward.Y, 5);
        Assert.Equal(-1f, forward.Z, 5);
        Assert.Equal(1f, up.X, 5);
        Assert.Equal(0f, up.Y, 5);
        Assert.Equal(0f, up.Z, 5);
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
        // Ten units along the facing derived above: (-6.1237244, 5, -6.1237244).
        var ahead = FreeCamMotion.LookAtFrom(Vector3.Zero, MathF.PI / 4f, MathF.PI / 6f);

        Assert.Equal(-6.1237244f, ahead.X, 4);
        Assert.Equal(5f, ahead.Y, 4);
        Assert.Equal(-6.1237244f, ahead.Z, 4);
    }

    [Fact]
    public void LookAtFromARotationSitsTenUnitsAlongItsFacing()
    {
        // Ten units along the facing derived above, from (1,2,3): (-5.1237244, 7, -3.1237244).
        var ahead = FreeCamMotion.LookAtFrom(new Vector3(1, 2, 3), Yaw45Pitch30);

        Assert.Equal(-5.1237244f, ahead.X, 4);
        Assert.Equal(7f, ahead.Y, 4);
        Assert.Equal(-3.1237244f, ahead.Z, 4);
    }

    [Fact]
    public void PitchUpRaisesTheLookAtTarget()
    {
        var level = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0f);
        var raised = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0.5f);
        Assert.True(raised.Y > level.Y);
    }

    /// <summary>One frame of free cam input: keys held (-1, 0 or 1 along forward, up and right), how far the mouse turned it, how far it rolled, its speed and the frame's seconds.</summary>
    private readonly record struct Flight(
        Vector3 Input,
        float Yaw,
        float Pitch,
        float Roll,
        float Speed,
        float Seconds
    );

    private static readonly Gen<float> AnyKey = Gen.Int[-1, 1].Select(k => (float)k);

    // Speed up to 128 yalms a second: the free cam's 8, times its fastest step of 4, times 4 for Shift.
    private static readonly Gen<Flight> AnyFlight = Gen.Select(
        Gen.Select(AnyKey, AnyKey, AnyKey, (f, u, r) => new Vector3(f, u, r)),
        Gen.Float[-MathF.PI, MathF.PI],
        Gen.Float[-MathF.PI / 2f, MathF.PI / 2f],
        Gen.Float[-MathF.PI, MathF.PI],
        Gen.Float[0f, 128f],
        AnyFrameStep,
        (input, yaw, pitch, roll, speed, seconds) => new Flight(input, yaw, pitch, roll, speed, seconds)
    );

    [Fact]
    [Trait("Category", "Property")]
    public void EveryFreeCamFrameIsWellFormed()
    {
        // Each frame as the plugin's free cam makes it: roll, then turn by the mouse, then fly, then the frame.
        (
            from position in AnyPosition
            from yaw in Gen.Float[-MathF.PI, MathF.PI]
            from pitch in Gen.Float[-EditLimits.PitchLimit, EditLimits.PitchLimit]
            from roll in Gen.Float[-MathF.PI, MathF.PI]
            from fov in Gen.Float[EditLimits.MinFov, EditLimits.MaxFov]
            from flights in AnyFlight.Array[1, 200]
            select (
                Position: position,
                Rotation: CameraRotation.FromAngles(yaw, pitch, roll),
                Fov: fov,
                Flights: flights
            )
        ).Sample(
            start =>
            {
                var (position, rotation) = (start.Position, start.Rotation);
                for (var i = 0; i < start.Flights.Length; i++)
                {
                    var flight = start.Flights[i];
                    rotation = FreeCamMotion.Roll(rotation, flight.Roll);
                    rotation = FreeCamMotion.Turn(rotation, flight.Yaw, flight.Pitch);
                    position = FreeCamMotion.Step(position, flight.Input, rotation, flight.Speed, flight.Seconds);
                    AssertWellFormed(CameraState.FromRotation(position, rotation, start.Fov), $"Frame {i}");
                }
            },
            iter: 1000,
            print: Kept<(Vector3 Position, Quaternion Rotation, float Fov, Flight[] Flights)>(start =>
                $"Start: {start.Position}, {start.Rotation}, {start.Fov}\nFlights: {string.Join("\n", start.Flights)}"
            )
        );
    }
}
