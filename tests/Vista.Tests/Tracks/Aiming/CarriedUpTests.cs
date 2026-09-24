using System.Numerics;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class CarriedUpTests
{
    // Travelling a yalm a second, so settling never waits.
    private static float Moving(double time) => (float)time;

    // Facing at yaw θ on the level: (-sin θ, 0, -cos θ).
    private static Vector3 Level(double yaw) => new(-(float)Math.Sin(yaw), 0f, -(float)Math.Cos(yaw));

    [Fact]
    public void ALevelTurnKeepsUpUpright()
    {
        // Turning a quarter about the vertical carries up about the vertical, which leaves it where it was.
        var carried = CarriedUp.Along(t => Level(t * MathF.PI / 4), Moving, 2.0, allowInverted: true, Vector3.UnitY);

        Near(Vector3.UnitY, carried.At(2.0, Level(MathF.PI / 2), Moving(2.0)), 1e-5f);
    }

    [Fact]
    public void AVerticalLoopIsInvertedAtItsTop()
    {
        // The facing turns a full circle about +x over 4 s, starting along -z: at 2 s it faces +z, half a turn on. Carrying
        // turns up half a turn with it, from (0, 1, 0) to (0, -1, 0); settling toward the nearer of upright and inverted
        // pulls it toward inverted there, so it stays inverted.
        static Vector3 Loop(double t)
        {
            var angle = t * Math.PI / 2;
            return new Vector3(0f, (float)Math.Sin(angle), -(float)Math.Cos(angle));
        }

        var carried = CarriedUp.Along(t => Loop(t), Moving, 4.0, allowInverted: true, Vector3.UnitY);

        Near(-Vector3.UnitY, carried.At(2.0, Loop(2.0), Moving(2.0)), 1e-2f);
    }

    [Fact]
    public void ASpiralThatLevelsOutSettlesUpright()
    {
        // Climbing at 45° while turning about the vertical tilts the carried up; five seconds of level travel settles it
        // with a one-second time constant, to within e^-5 < 1% of the tilt: well inside 1°.
        static Vector3 Spiral(double t)
        {
            var yaw = t * 0.6;
            var pitch = t < 5 ? Math.PI / 4 : 0;
            return new Vector3(
                -(float)(Math.Sin(yaw) * Math.Cos(pitch)),
                (float)Math.Sin(pitch),
                -(float)(Math.Cos(yaw) * Math.Cos(pitch))
            );
        }

        var carried = CarriedUp.Along(t => Spiral(t), Moving, 10.0, allowInverted: true, Vector3.UnitY);
        var up = carried.At(10.0, Spiral(10.0), Moving(10.0));

        Assert.InRange(MathF.Acos(Math.Clamp(Vector3.Dot(up, Vector3.UnitY), -1f, 1f)), 0f, 1f * Deg);
    }

    [Fact]
    public void ASnapTurnsAboutTheCurrentUp()
    {
        // The facing turns straight back in one sample, a doubleback: up stays (0, 1, 0), so the picture stays upright.
        var carried = CarriedUp.Along(
            t => t < 1 ? Level(0) : Level(Math.PI),
            Moving,
            2.0,
            allowInverted: true,
            Vector3.UnitY
        );

        Near(Vector3.UnitY, carried.At(2.0, Level(Math.PI), Moving(2.0)), 1e-5f);
    }

    [Fact]
    public void ATrackStartingStraightUpTakesTheGivenUp()
    {
        // Facing straight up there's no upright, so the start takes the up it's given.
        var start = new Vector3(0f, 0f, 1f);
        var carried = CarriedUp.Along(_ => Vector3.UnitY, Moving, 1.0, allowInverted: true, start);

        Near(start, carried.At(0.5, Vector3.UnitY, Moving(0.5)), 1e-5f);
    }

    [Fact]
    public void AHoldStaysStillThoughItIsTilted()
    {
        // A spiral climb tilts the carried up, then the camera holds, not travelling: settling waits, so up doesn't move.
        static Vector3 Climb(double t) =>
            new(
                -(float)(Math.Sin(t * 0.6) * Math.Cos(0.7)),
                (float)Math.Sin(0.7),
                -(float)(Math.Cos(t * 0.6) * Math.Cos(0.7))
            );
        var carried = CarriedUp.Along(t => Climb(Math.Min(t, 3)), t => (float)Math.Min(t, 3), 6.0, true, Vector3.UnitY);

        Assert.Equal(carried.At(3.5, Climb(3), 3f), carried.At(5.5, Climb(3), 3f));
    }

    [Fact]
    public void SettlingTowardUprightTurnsAnInvertedPictureBackRound()
    {
        // Held level and inverted with inversion not allowed: the up turns back about the facing toward upright, halfway
        // or more within a second (the settling is exponential in the angle, from π to under π·e^-1 ≈ 66°).
        var up = -Vector3.UnitY;
        for (var step = 0; step < 100; step++)
            up = CarriedUp.Settle(up, Level(0), 0.01f, allowInverted: false);

        Assert.InRange(MathF.Acos(Math.Clamp(Vector3.Dot(up, Vector3.UnitY), -1f, 1f)), 0f, 67f * Deg);
    }
}
