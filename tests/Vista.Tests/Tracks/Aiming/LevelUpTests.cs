using System.Numerics;
using Vista.Core.Camera;
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

    // Each test's facing turns 90° a second, 0.09° a millisecond, and keeping level may turn up at most SpinPerTurn (10) times that, 0.9°.
    private const float MostStepDegrees = 0.09f * SpinPerTurn;

    // The largest angle, in degrees, up turns between samples a millisecond apart.
    private static float LargestStep(LevelUp level, Func<double, Vector3> facing, double duration)
    {
        var largest = 0f;
        var last = level.At(0.0, facing(0.0));
        for (var t = 0.001; t <= duration; t += 0.001)
        {
            var up = level.At(t, facing(t));
            largest = MathF.Max(largest, Vectors.AngleBetween(last, up) / Deg);
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
        Assert.InRange(LargestStep(level, t => OverTheTop(t * Math.PI / 2), 4.0), 0f, MostStepDegrees);
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
        Assert.InRange(LargestStep(level, Crane, 4.0), 0f, MostStepDegrees);
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
        Assert.InRange(LargestStep(level, t => OverTheTop(t * Math.PI / 2), 2.0), 0f, MostStepDegrees);
    }

    [Fact]
    public void LookAtUnderItsPointTurnsRoundFromPointToPoint()
    {
        // The regression scene's shot: along x at 5 yalms a second from -10 to 20, passing points at x = -10, 0 and 20 (0, 2
        // and 6 s), facing its point at (0, 10, 0): (-x, 10, 0). The view is α = atan2(10, -x) from +x, 45° at the first
        // point. The passage is |x| < 10 tan 15° = 2.68; its span starts at the first point, the last left before it, and
        // ends where the view leaves 60° from straight up, x = 10 / tan 30° = 17.32 (α = 150°), short of the last point
        // at 26.6° up. The lean turns from -x (back from the first point's climb) half round to +x over those 105°.
        // Squared to the view at x, a lean (-cos θ, 0, ±sin θ) is (-100 cos θ / r², -10 x cos θ / r², ±sin θ), r² = x² + 100,
        // made unit. The turn is either way round, so z's sign isn't pinned.
        static Vector3 Facing(double t) => new(10f - (5f * (float)t), 10f, 0f);
        float[] times = [0f, 2f, 6f];
        var level = LevelUp.Along(t => Facing(t), 6f, allowInverted: false, Vector3.UnitY, (times, times));

        // At the first point, upright leans back along -x: (-1, 1, 0) / √2.
        Near(new Vector3(-1f, 1f, 0f) / MathF.Sqrt(2f), level.At(0.0, Facing(0.0)), 1e-6f);

        // The span's end is found within 0.78 ms (0.1 s halved 7 times), where the view turns 5·10/400 = 0.125 rad a
        // second, so the share is out by under 1e-4 / 1.83 rad, and the up by under 1e-3.
        // At x = -5 (1 s), α = 63.435°: share (63.435 - 45) / 105 = 0.17557, eased 3s² - 2s³ = 0.081651, θ = 14.697°;
        // squared, (-0.8 cos θ, 0.4 cos θ, ±sin θ) / √(0.8 cos² θ + sin² θ) = (-0.85828, 0.42914, ±0.28140).
        var before = level.At(1.0, Facing(1.0));
        Assert.Equal(-0.85828f, before.X, 1e-3f);
        Assert.Equal(0.42914f, before.Y, 1e-3f);
        Assert.Equal(0.28140f, MathF.Abs(before.Z), 1e-3f);

        // At x = 5 (3 s), past the passage, still turning: α = 116.565°, share 0.68157, eased 0.76039, θ = 136.869°;
        // squared, (-0.8 cos θ, -0.4 cos θ, ±sin θ) / √(0.8 cos² θ + sin² θ) = (0.61766, 0.30883, ±0.72327).
        var after = level.At(3.0, Facing(3.0));
        Assert.Equal(0.61766f, after.X, 1e-3f);
        Assert.Equal(0.30883f, after.Y, 1e-3f);
        Assert.Equal(0.72327f, MathF.Abs(after.Z), 1e-3f);

        // From x = 17.32 on the turn is done and the up is upright, leaning back along +x: (10, x, 0) made unit, at x = 17.5
        // (5.5 s) and at the last point, x = 20.
        Near(new Vector3(10f, 17.5f, 0f) / MathF.Sqrt(406.25f), level.At(5.5, Facing(5.5)), 1e-6f);
        Near(new Vector3(10f, 20f, 0f) / MathF.Sqrt(500f), level.At(6.0, Facing(6.0)), 1e-6f);
    }

    [Fact]
    public void ATurnSpanStopsAtSixtyDegreesRatherThanAtAFarPoint()
    {
        // Over the top from level along -z to level along +z at 90° a second, with points only at the ends (0 and 2 s). The
        // span would start at the first point, but reaches no further than 60° from straight up: 30° over, at 1/3 s, to
        // 150°, at 5/3 s. Before it, the up is upright: at 22.5° over (0.25 s), (0, cos 22.5°, sin 22.5°).
        float[] times = [0f, 2f];
        var level = LevelUp.Along(
            t => OverTheTop(t * Math.PI / 2),
            2f,
            allowInverted: false,
            Vector3.UnitY,
            (times, times)
        );

        Near(new Vector3(0f, 0.92388f, 0.38268f), level.At(0.25, OverTheTop(Math.PI / 8)), 1e-5f);

        // The lean turns from +z half round to -z over the span's 120°. At 45° over (0.5 s): share (45 - 30) / 120 = 0.125,
        // eased 3s² - 2s³ = 0.042969, θ = 7.7344°; the lean (±sin θ, 0, cos θ) squared to (0, sin 45°, -cos 45°) is
        // (±0.13458, 0.49545, 0.49545) / 0.71348 = (±0.18863, 0.69441, 0.69441). Spanning from the first point instead, it
        // would be turned 28° by now. The span's start is found within 0.78 ms (0.1 s halved 7 times), 0.07°: the share is
        // out by up to 0.07 × 105 / 120² = 5e-4, eased at 6s(1 - s) = 0.66 times, turning the lean up to π × 3.3e-4 = 1e-3
        // rad, and the squared up, over 0.71 long, up to 1.5e-3.
        var up = level.At(0.5, OverTheTop(Math.PI / 4));
        Assert.Equal(0.18863f, MathF.Abs(up.X), 2e-3f);
        Assert.Equal(0.69441f, up.Y, 2e-3f);
        Assert.Equal(0.69441f, up.Z, 2e-3f);
    }

    [Fact]
    public void TurnSpansThatWouldOverlapSplitAtTheMidpointBetweenThePassages()
    {
        // Over the top from 45° up along -z to 45° up along +z in the first second, then back: two passages (1/3 to 2/3 s
        // and 4/3 to 5/3 s) with the view within 60° of straight up throughout, and points only at the ends. Merged, the
        // lean would turn from +z and back to +z, not at all. Instead the time between the first passage's end (2/3 s)
        // and the second's start (4/3 s) splits at its midpoint, 1 s, 135° over: the first span runs 0 to 1 s and the
        // second 1 to 2 s, each over 90° of view, each a half turn of the lean (+z to -z, then back).
        static Vector3 Facing(double t) => OverTheTop((t < 1 ? 45 + (90 * t) : 135 - (90 * (t - 1))) * Deg);
        float[] times = [0f, 2f];
        var level = LevelUp.Along(t => Facing(t), 2f, allowInverted: false, Vector3.UnitY, (times, times));

        // Each passage edge is found within 0.78 ms (0.1 s halved 7 times) outside the gap, so the split lies within 0.39
        // ms, 0.035°, of 135°. At 0.25 s, 67.5° over, the first span has turned 22.5° of its 90°: share 0.25 (out by up to
        // 0.25 × 0.035 / 90 = 1e-4), eased 3s² - 2s³ = 0.15625 (slope 1.125), θ = 28.125° (out by up to π × 1.1e-4 = 3.5e-4
        // rad). The lean (±sin θ, 0, cos θ) = (±0.47140, 0, 0.88192) squared to (0, sin 67.5°, -cos 67.5°) is
        // (±0.50078, 0.33124, 0.79969).
        var first = level.At(0.25, Facing(0.25));
        Assert.Equal(0.50078f, MathF.Abs(first.X), 1e-3f);
        Assert.Equal(0.33124f, first.Y, 1e-3f);
        Assert.Equal(0.79969f, first.Z, 1e-3f);

        // At the split, 135° over, both turns are done or not begun: upright, (0, √½, -√½). Within 0.035° of it, the
        // eased share is within 3 × (0.035 / 90)² = 5e-7 of 0 or 1.
        Near(new Vector3(0f, MathF.Sqrt(0.5f), -MathF.Sqrt(0.5f)), level.At(1.0, Facing(1.0)), 1e-5f);

        // At 1.5 s, straight up, the second span has turned 45° of its 90° from -z: share 0.5 (out by up to 0.035 × 45 / 90²
        // = 2e-4), eased 0.5 (slope 1.5), a quarter turn (out by up to π × 3e-4 = 1e-3 rad); facing straight up the lean
        // is the up, (±1, 0, 0). Clipped at the second passage's start instead, it would be 28° from -z.
        var second = level.At(1.5, Facing(1.5));
        Assert.Equal(1f, MathF.Abs(second.X), 2e-3f);
        Assert.Equal(0f, second.Z, 2e-3f);

        // At 1.75 s, 67.5° over again, the second span has turned 67.5° of 90°: share 0.75, eased 0.84375, θ = 151.875°
        // from -z, the lean (∓sin θ, 0, -cos θ) = (∓0.47140, 0, 0.88192), squared as at 0.25 s: (∓0.50078, 0.33124, 0.79969).
        var late = level.At(1.75, Facing(1.75));
        Assert.Equal(0.50078f, MathF.Abs(late.X), 1e-3f);
        Assert.Equal(0.33124f, late.Y, 1e-3f);
        Assert.Equal(0.79969f, late.Z, 1e-3f);

        // At the end, 45° over along -z, upright: (0, cos 45°, sin 45°).
        Near(new Vector3(0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)), level.At(2.0, Facing(2.0)), 1e-5f);
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
