using System.Numerics;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class LevelUpTests
{
    // Facing at yaw θ on the level: (-sin θ, 0, -cos θ).
    private static Vector3 Level(double yaw) => new(-(float)Math.Sin(yaw), 0f, -(float)Math.Cos(yaw));

    // Facing at yaw 0 (along -z) pitched up by angle a, turning over the top past a = π/2: (0, sin a, -cos a).
    private static Vector3 OverTheTop(double a) => new(0f, (float)Math.Sin(a), -(float)Math.Cos(a));

    // The largest angle, in degrees, up turns between samples a millisecond apart. Each test's facing turns 90° a second,
    // 0.09° a millisecond, and keeping level may turn up at most 10 times that (Fixtures.SpinPerTurn), 0.9°.
    private static float LargestStep(LevelUp level, Func<double, Vector3> facing, double duration)
    {
        var largest = 0f;
        var last = level.At(0.0, facing(0.0));
        for (var t = 0.001; t <= duration; t += 0.001)
        {
            var up = level.At(t, facing(t));
            largest = MathF.Max(largest, MathF.Acos(Math.Clamp(Vector3.Dot(last, up), -1f, 1f)) / Deg);
            last = up;
        }

        return largest;
    }

    [Fact]
    public void ALevelTurnStaysUpright()
    {
        // A level facing's upright up is world up, whichever way it turns.
        var level = LevelUp.Along(t => Level(t * MathF.PI / 4), 2f, allowInverted: true, Vector3.UnitY);

        Near(Vector3.UnitY, level.At(2.0, Level(MathF.PI / 2)), 1e-6f);
    }

    [Fact]
    public void AClimbingTurnKeepsTheHorizonExactlyLevel()
    {
        // Climbing at 45° while turning about the vertical never nears straight up, so up is upright throughout: facing
        // (-sin y cos 45°, sin 45°, -cos y cos 45°), world up made square to it is (sin y sin 45°, cos 45°, cos y sin 45°).
        static Vector3 Spiral(double t) =>
            new(
                -(float)(Math.Sin(t * 0.8) * Math.Cos(Math.PI / 4)),
                (float)Math.Sin(Math.PI / 4),
                -(float)(Math.Cos(t * 0.8) * Math.Cos(Math.PI / 4))
            );
        var level = LevelUp.Along(t => Spiral(t), 5f, allowInverted: true, Vector3.UnitY);

        for (var t = 0.0; t <= 5.0; t += 0.25)
        {
            var expected = new Vector3(
                (float)(Math.Sin(t * 0.8) * Math.Sin(Math.PI / 4)),
                (float)Math.Cos(Math.PI / 4),
                (float)(Math.Cos(t * 0.8) * Math.Sin(Math.PI / 4))
            );
            Near(expected, level.At(t, Spiral(t)), 1e-5f);
        }
    }

    [Fact]
    public void AVerticalLoopIsUpsideDownAtItsTopAndUprightAfter()
    {
        // A full turn about +x over 4 s from along -z: straight up at 1 s, along +z at 2 s, straight down at 3 s. The way
        // out of each vertical passage is the reverse of the way in, so the first turns the track upside down and the
        // second rights it: level facing +z, up is -y; back along -z, up is +y.
        var level = LevelUp.Along(t => OverTheTop(t * Math.PI / 2), 4f, allowInverted: true, Vector3.UnitY);

        Near(-Vector3.UnitY, level.At(2.0, OverTheTop(Math.PI)), 1e-6f);
        Near(Vector3.UnitY, level.At(4.0, OverTheTop(2 * Math.PI)), 1e-6f);
        Assert.InRange(LargestStep(level, t => OverTheTop(t * Math.PI / 2), 4.0), 0f, 0.9f);
    }

    [Fact]
    public void ACraneThatWobblesPastStraightUpComesOutUpright()
    {
        // Up along -z to straight up in the first second, then 8° past it toward +z and back, then 8° toward -x and
        // back, then down along +x to level. The way out (+x) is a quarter turn from the way in (-z), short of 135°, so
        // it comes out upright: level along +x, up is +y. The quarter turn is spread over the passage, so up never steps.
        const double wobble = 8.0 * Math.PI / 180.0;
        static Vector3 Crane(double t) =>
            t switch
            {
                < 1 => OverTheTop(t * Math.PI / 2),
                < 2 => OverTheTop((Math.PI / 2) + (wobble * Math.Sin((t - 1) * Math.PI))),
                < 3 => new Vector3(
                    -(float)Math.Sin(wobble * Math.Sin((t - 2) * Math.PI)),
                    (float)Math.Cos(wobble * Math.Sin((t - 2) * Math.PI)),
                    0f
                ),
                _ => new Vector3((float)Math.Sin((t - 3) * Math.PI / 2), (float)Math.Cos((t - 3) * Math.PI / 2), 0f),
            };
        var level = LevelUp.Along(t => Crane(t), 4f, allowInverted: true, Vector3.UnitY);

        Near(Vector3.UnitY, level.At(4.0, Crane(4.0)), 1e-6f);
        Assert.InRange(LargestStep(level, Crane, 4.0), 0f, 0.9f);
    }

    [Fact]
    public void PassingOverTheTopWithoutInvertingTurnsRoundAboutTheVertical()
    {
        // As a target aim does passing under its point: over the top from along -z to along +z, never inverting. Up
        // leans +z on the way in and -z on the way out, half a turn about the vertical, so straight up at the passage's
        // middle it has turned a quarter, leaning along ±x; level along +z it's upright again.
        var level = LevelUp.Along(t => OverTheTop(t * Math.PI / 2), 2f, allowInverted: false, Vector3.UnitY);

        var middle = level.At(1.0, OverTheTop(Math.PI / 2));
        Assert.Equal(1f, MathF.Abs(middle.X), 1e-3f);
        Assert.Equal(0f, middle.Z, 1e-3f);
        Near(Vector3.UnitY, level.At(2.0, OverTheTop(Math.PI)), 1e-6f);
        Assert.InRange(LargestStep(level, t => OverTheTop(t * Math.PI / 2), 2.0), 0f, 0.9f);
    }

    [Fact]
    public void AHoldInsideAPassageStaysStill()
    {
        // Up to 85° over the first second, still until 3 s, then over the top: up only turns as the facing does, so it
        // doesn't move while the facing holds still.
        static Vector3 Climb(double t) =>
            OverTheTop(
                t < 1 ? t * 85 * Deg
                : t < 3 ? 85 * Deg
                : (85 * Deg) + ((t - 3) * 90 * Deg)
            );
        var level = LevelUp.Along(t => Climb(t), 5f, allowInverted: true, Vector3.UnitY);

        Assert.Equal(level.At(1.0, Climb(1.0)), level.At(2.5, Climb(2.5)));
    }

    [Fact]
    public void ATrackStartingStraightUpTakesTheGivenUp()
    {
        // Facing straight up there's no upright, so the start takes the up it's given.
        var start = new Vector3(0f, 0f, 1f);
        var level = LevelUp.Along(_ => Vector3.UnitY, 1f, allowInverted: true, start);

        Near(start, level.At(0.5, Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void ATrackEndingInAPassageKeepsTheUpItEnteredWith()
    {
        // Up along -z to straight up at 1 s, then straight up to the end: up leans +z as it enters (upright facing up
        // along -z), and facing straight up that's (0, 0, 1). The up for a track starting straight up, (1, 0, 0), plays no
        // part.
        var level = LevelUp.Along(
            t => OverTheTop(Math.Min(t, 1) * Math.PI / 2),
            2f,
            allowInverted: true,
            Vector3.UnitX
        );

        Near(Vector3.UnitZ, level.At(2.0, Vector3.UnitY), 1e-5f);
    }

    [Fact]
    public void ATrackStartingNearlyStraightUpStartsLevel()
    {
        // Climbing at 84°, facing (1, 10, 0)/√101, the whole shot, inside a passage it never leaves: there is a level up,
        // square to the facing in the x-y plane and leaning back, (-10, 1, 0)/√101, so the up for a shot starting exactly
        // straight up, (0, 0, 1), plays no part.
        var facing = Vector3.Normalize(new Vector3(1f, 10f, 0f));
        var level = LevelUp.Along(_ => facing, 2f, allowInverted: true, Vector3.UnitZ);

        Near(new Vector3(-10f, 1f, 0f) / MathF.Sqrt(101f), level.At(1.0, facing), 1e-5f);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    [InlineData(180.0)]
    [InlineData(270.0)]
    public void SnappingFromLevelIntoAPassageTurnsOverWhicheverWayItHeads(double heading)
    {
        // Level for a second, then snapping straight up and tipping on over the top to level the other way. Level has no
        // lean to start the passage from, so it leans back from the climb; the way out is reversed from that, so the track
        // turns upside down, whichever way it heads: level at the end, up is (0, -1, 0).
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(heading * Math.PI / 180));
        Vector3 Facing(double t) =>
            Vector3.Transform(
                t < 1 ? new Vector3(0f, 0f, -1f) : OverTheTop(Math.PI / 2 + ((t - 1) * Math.PI / 2)),
                turn
            );
        var level = LevelUp.Along(t => Facing(t), 2f, allowInverted: true, Vector3.UnitY);

        Near(-Vector3.UnitY, level.At(2.0, Facing(2.0)), 1e-5f);
    }

    [Fact]
    public void ATrackStartingStraightDownTakesTheGivenUpNegated()
    {
        // Facing straight down, the given up for straight up, (0, 0, 1), is negated: (0, 0, -1).
        var level = LevelUp.Along(_ => -Vector3.UnitY, 1f, allowInverted: true, Vector3.UnitZ);

        Near(-Vector3.UnitZ, level.At(0.5, -Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void APassageLeftInTheShotsLastStepStillEndsLevel()
    {
        // Over the top at 90° a second for 1.175 s, never inverting: the facing leaves the passage (105° over) at 1.167 s,
        // in the shot's last step, and ends at 105.75° over, (0, sin 105.75°, -cos 105.75°) = (0, 0.96246, 0.27144). Leaning
        // along +z past the top, its upright up is (0, 0.27144, -0.96246).
        var level = LevelUp.Along(t => OverTheTop(t * Math.PI / 2), 1.175f, allowInverted: false, Vector3.UnitY);

        Near(new Vector3(0f, 0.27144f, -0.96246f), level.At(1.175, OverTheTop(1.175 * Math.PI / 2)), 1e-4f);
    }
}
