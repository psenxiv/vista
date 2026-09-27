using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Timing;

public class TimingCurveTests
{
    private static TimingKey Key(
        float time,
        float position,
        TangentMode mode = TangentMode.Auto,
        float inTangent = 0f,
        float outTangent = 0f
    ) => new(time, position, mode, mode, inTangent, outTangent);

    private static TimingKey Sided(
        float time,
        float position,
        TangentMode inMode,
        TangentMode outMode,
        float inTangent = 0f,
        float outTangent = 0f,
        bool broken = false
    ) => new(time, position, inMode, outMode, inTangent, outTangent, broken);

    [Fact]
    public void ZeroKeysGiveZeroDurationAndZeroPosition()
    {
        var curve = new TimingCurve(Array.Empty<TimingKey>());
        Assert.Equal(0.0, curve.Duration);
        Assert.Equal(0f, curve.PositionAt(-3));
        Assert.Equal(0f, curve.PositionAt(0));
        Assert.Equal(0f, curve.PositionAt(5));
    }

    [Fact]
    public void OneKeyAlwaysReturnsItsPosition()
    {
        var keys = new[] { Key(2f, 5f) };
        var curve = new TimingCurve(keys);

        Assert.Equal(2.0, curve.Duration);
        Assert.Equal(5f, curve.PositionAt(-10));
        Assert.Equal(5f, curve.PositionAt(0));
        Assert.Equal(5f, curve.PositionAt(2));
        Assert.Equal(5f, curve.PositionAt(100));
    }

    [Fact]
    public void ConstructorThrowsWhenTimeDoesNotStrictlyIncrease()
    {
        var keys = new[] { Key(0f, 0f), Key(0f, 1f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("Timing keys must have strictly increasing times.", ex.Message);
    }

    [Fact]
    public void ConstructorRejectsAnUnknownTangentMode()
    {
        var keys = new[] { Key(0f, 0f, (TangentMode)99), Key(1f, 1f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("Timing key 0 has an unknown tangent mode.", ex.Message);
    }

    [Fact]
    public void PositionAtKeepsFullPrecisionOnLongShots()
    {
        // Keys 0.0625 s apart, one float step at a million seconds. A time cast to float
        // rounds 1_000_000.04 up to the next key and evaluates the wrong interval.
        var keys = new[] { Key(0f, 0f), Key(1_000_000f, 1f), Key(1_000_000.0625f, 1f), Key(1_000_000.125f, 2f) };
        var curve = new TimingCurve(keys);

        Assert.Equal(1f, curve.PositionAt(1_000_000.04), 4);
    }

    [Fact]
    public void ConstructorThrowsWhenTimeDecreases()
    {
        var keys = new[] { Key(1f, 0f), Key(0f, 1f) };
        Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
    }

    [Fact]
    public void ConstructorThrowsWhenPositionDecreases()
    {
        var keys = new[] { Key(0f, 1f), Key(1f, 0f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("Timing key positions must not decrease.", ex.Message);
    }

    [Fact]
    public void ConstructorAllowsAHoldTwoKeysSamePositionDifferentTimes()
    {
        var keys = new[] { Key(0f, 0f), Key(1f, 0f), Key(2f, 3f) };
        var exception = Record.Exception(() => new TimingCurve(keys));
        Assert.Null(exception);
    }

    [Fact]
    public void DurationIsTheLastKeysTime()
    {
        var keys = new[] { Key(0f, 0f), Key(1.5f, 1f), Key(4f, 3f) };
        var curve = new TimingCurve(keys);
        Assert.Equal(4.0, curve.Duration);
    }

    [Fact]
    public void HoldsBeforeFirstKeyAndAfterLastKey()
    {
        var keys = new[] { Key(1f, 2f), Key(2f, 5f) };
        var curve = new TimingCurve(keys);

        Assert.Equal(2f, curve.PositionAt(-5));
        Assert.Equal(2f, curve.PositionAt(1));
        Assert.Equal(5f, curve.PositionAt(2));
        Assert.Equal(5f, curve.PositionAt(50));
    }

    [Fact]
    public void AFlatSectionHoldsTheCameraStillForItsWidth()
    {
        var keys = new[] { Key(0f, 0f), Key(1f, 0f), Key(2f, 2f) };
        var curve = new TimingCurve(keys);

        for (var t = 0.0; t <= 1.0; t += 0.1)
            Assert.Equal(0f, curve.PositionAt(t), 5);
    }

    [Fact]
    public void AHoldFarAlongThePathStaysExactlyOnItsKey()
    {
        // Both keys sit at 119.6109, so every time inside the hold is at 119.6109. No tolerance:
        // one float step here is ~0.0000076, and a wobble of that step shimmers a look-ahead-0 aim.
        const float held = 119.6109f;
        const float start = 11.5663f;
        const float end = 12.2936f;
        var keys = new[] { Key(0f, 0f), Key(start, held), Key(end, held), Key(14f, 130f) };
        var curve = new TimingCurve(keys);

        for (double t = start; t <= end; t += 1e-5)
            Assert.Equal(held, curve.PositionAt(t));
    }

    [Fact]
    public void AFlatModeKeyBringsSpeedToZeroAtThatKey()
    {
        var keys = new[] { Key(0f, 0f), Key(1f, 1f, TangentMode.Flat), Key(2f, 3f) };
        var curve = new TimingCurve(keys);

        const double eps = 1e-3;
        var speedBefore = (curve.PositionAt(1.0) - curve.PositionAt(1.0 - eps)) / eps;
        var speedAfter = (curve.PositionAt(1.0 + eps) - curve.PositionAt(1.0)) / eps;

        Assert.True(Math.Abs(speedBefore) < 0.01, $"speed before the Flat key should be ~0, got {speedBefore}");
        Assert.True(Math.Abs(speedAfter) < 0.01, $"speed after the Flat key should be ~0, got {speedAfter}");
    }

    [Fact]
    public void TheCurveNeverDecreasesIncludingKeysANaiveCubicWouldOvershoot()
    {
        // Flat-steep-flat: a naive average-of-secants tangent at keys 1 and 2 would be
        // nonzero, overshooting below 0 near key 1 and above 1 near key 2.
        var keys = new[] { Key(0f, 0f), Key(1f, 0f), Key(2f, 1f), Key(3f, 1f) };
        var curve = new TimingCurve(keys);

        var previous = curve.PositionAt(0);
        for (var t = 0.0; t <= 3.0; t += 0.01)
        {
            var pos = curve.PositionAt(t);
            Assert.True(pos >= previous - 1e-4f, $"position decreased at t={t}: {pos} < {previous}");
            Assert.True(pos is >= -1e-4f and <= 1f + 1e-4f, $"position overshot [0,1] at t={t}: {pos}");
            previous = pos;
        }
    }

    [Fact]
    public void SmoothEndsOnARisingTrackStartAtRestAndEndFast()
    {
        // Intervals of 1 s averaging 1 and 3 (SciPy PCHIP's end rule, spec §2):
        // start ((2·1 + 1)·1 − 1·3) / 2 = 0; end ((2·1 + 1)·3 − 1·1) / 2 = 4, within the last interval's bound of 3 × 3 = 9.
        var keys = new[] { Key(0f, 0f), Key(1f, 1f), Key(2f, 4f) };
        var curve = new TimingCurve(keys);

        Assert.Equal(0f, curve.SideSlope(0, KeySide.Out), 1e-4f);
        Assert.Equal(4f, curve.SideSlope(2, KeySide.In), 1e-4f);
    }

    [Fact]
    public void SmoothEndsFollowTheTrendOfTheirTwoNearestLegs()
    {
        // Intervals of 2 s averaging 5 and 4 s averaging 2.5 (SciPy PCHIP's end rule, spec §2):
        // first key ((2·2 + 4)·5 − 2·2.5) / (2 + 4) = 35/6; last key ((2·4 + 2)·2.5 − 4·5) / (4 + 2) = 5/6.
        var curve = new TimingCurve([Key(0f, 0f), Key(2f, 10f), Key(6f, 20f)]);

        Assert.Equal(35f / 6f, curve.SideSlope(0, KeySide.Out), 1e-4f);
        Assert.Equal(5f / 6f, curve.SideSlope(2, KeySide.In), 1e-4f);
    }

    [Fact]
    public void ASmoothEndStartsAtRestWhereTheTrendWouldRunBackwards()
    {
        // Averages 5 then 50 over 2 s each: ((2·2 + 2)·5 − 2·50) / 4 = −17.5, the wrong sign, so the first key starts at 0.
        var curve = new TimingCurve([Key(0f, 0f), Key(2f, 10f), Key(4f, 110f)]);

        Assert.Equal(0f, curve.SideSlope(0, KeySide.Out));
    }

    [Fact]
    public void SmoothEndsKeepTheLegsSpeedWithTwoKeysOrEvenLegs()
    {
        // Two keys: the end rule needs two intervals, so both ends keep the secant, 10 / 2 = 5.
        var two = new TimingCurve([Key(0f, 0f), Key(2f, 10f)]);
        Assert.Equal(5f, two.SideSlope(0, KeySide.Out), 1e-4f);
        Assert.Equal(5f, two.SideSlope(1, KeySide.In), 1e-4f);

        // Even legs, 5 yalms/s on 2 s and on 4 s: ((2·2 + 4)·5 − 2·5) / 6 = 30/6 = 5 at the first key, and 5 at the last.
        var even = new TimingCurve([Key(0f, 0f), Key(2f, 10f), Key(6f, 30f)]);
        Assert.Equal(5f, even.SideSlope(0, KeySide.Out), 1e-4f);
        Assert.Equal(5f, even.SideSlope(2, KeySide.In), 1e-4f);
    }

    [Fact]
    public void ASmoothLastKeyAfterAHoldStaysAtRest()
    {
        // The last interval is a hold (10 to 10), so δ₀ = 0 and the end slope is 0, as before.
        var curve = new TimingCurve([Key(0f, 0f), Key(2f, 10f), Key(4f, 10f)]);

        Assert.Equal(0f, curve.SideSlope(2, KeySide.In));
    }

    [Fact]
    public void ManualTangentsAreClampedToStayMonotone()
    {
        var keys = new[] { Key(0f, 0f, TangentMode.Manual, 0f, 10f), Key(1f, 1f, TangentMode.Manual, 10f, 0f) };
        var curve = new TimingCurve(keys);

        var previous = curve.PositionAt(0);
        for (var t = 0.0; t <= 1.0; t += 0.02)
        {
            var pos = curve.PositionAt(t);
            Assert.True(pos >= previous - 1e-4f, $"manual tangents let position decrease at t={t}: {pos} < {previous}");
            previous = pos;
        }
    }

    [Fact]
    public void LinearModeUsesPlainSecantsOnEitherSide()
    {
        var keys = new[] { Key(0f, 0f), Key(1f, 1f, TangentMode.Linear), Key(3f, 5f) };
        var curve = new TimingCurve(keys);

        const double eps = 1e-4;
        var speedLeft = (curve.PositionAt(1.0) - curve.PositionAt(1.0 - eps)) / eps;
        var speedRight = (curve.PositionAt(1.0 + eps) - curve.PositionAt(1.0)) / eps;

        Assert.Equal(1.0, speedLeft, 2);
        Assert.Equal(2.0, speedRight, 2);
    }

    [Fact]
    public void LopsidedLegsGiveEqualSpeedEitherSideOfAnInteriorAutoKey()
    {
        // Legs of very different width (1s / 10s / 1s) either side of key 1: the square
        // clamp must not couple the two intervals' ratios together, or key 1's in- and
        // out-tangent diverge and the pass-through stops being smooth (spec 255-257).
        var keys = new[] { Key(0f, 0f), Key(1f, 1f), Key(11f, 2f), Key(12f, 3f) };
        var curve = new TimingCurve(keys);

        const double eps = 1e-4;
        var speedBefore = (curve.PositionAt(1.0) - curve.PositionAt(1.0 - eps)) / eps;
        var speedAfter = (curve.PositionAt(1.0 + eps) - curve.PositionAt(1.0)) / eps;

        Assert.Equal(speedBefore, speedAfter, 3);
    }

    [Fact]
    public void LinearSidesMakeEachSpanStraight()
    {
        var curve = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Linear, TangentMode.Linear),
                Sided(2f, 4f, TangentMode.Linear, TangentMode.Linear),
                Sided(4f, 5f, TangentMode.Linear, TangentMode.Linear),
            }
        );

        Assert.Equal(2f, curve.PositionAt(1.0), 4);
        Assert.Equal(2f, curve.SlopeAt(1.0), 4);
        Assert.Equal(4.5f, curve.PositionAt(3.0), 4);
        Assert.Equal(0.5f, curve.SlopeAt(3.0), 4);
    }

    [Fact]
    public void AFlatInSideArrivesAtRestAndAFlatOutSideLeavesFromRest()
    {
        var curve = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Auto, TangentMode.Flat),
                Sided(2f, 4f, TangentMode.Flat, TangentMode.Auto),
            }
        );

        Assert.Equal(0f, curve.SideSlope(0, KeySide.Out));
        Assert.Equal(0f, curve.SideSlope(1, KeySide.In));
        Assert.True(curve.SlopeAt(0.001) < 0.05f);
        Assert.True(curve.SlopeAt(1.999) < 0.05f);
        Assert.True(curve.SlopeAt(1.0) > 2f);
    }

    [Fact]
    public void EachSideOfAKeyFollowsItsOwnMode()
    {
        var curve = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Auto, TangentMode.Auto),
                Sided(2f, 4f, TangentMode.Flat, TangentMode.Linear),
                Sided(4f, 5f, TangentMode.Auto, TangentMode.Auto),
            }
        );

        Assert.Equal(0f, curve.SideSlope(1, KeySide.In));
        Assert.Equal(0.5f, curve.SideSlope(1, KeySide.Out), 4);
    }

    [Fact]
    public void AManualSideIsARatioToItsSpansSecant()
    {
        var curve = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 0f, 0.5f),
                Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
            }
        );
        Assert.Equal(1f, curve.SideSlope(0, KeySide.Out), 4);

        var steep = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 0f, 100f),
                Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
            }
        );
        Assert.Equal(6f, steep.SideSlope(0, KeySide.Out), 4);
    }

    [Fact]
    public void AManualSideUsesItsTangentWithinTheMonotoneLimit()
    {
        var gentle = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 1f, 1f),
                Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
            }
        );
        Assert.Equal(2f, gentle.SideSlope(0, KeySide.Out), 4);

        var steep = new TimingCurve(
            new[]
            {
                Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 100f, 100f),
                Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
            }
        );
        Assert.Equal(6f, steep.SideSlope(0, KeySide.Out), 4);
    }

    [Fact]
    public void SlopeAtMatchesTheCurvesRateOfChange()
    {
        var curve = new TimingCurve(new[] { Key(0f, 0f), Key(2f, 4f), Key(5f, 5f), Key(6f, 9f) });
        foreach (var t in new[] { 0.5, 1.7, 3.2, 5.5 })
        {
            var (left, right) = Slopes(curve.PositionAt, t, 1e-3);
            var estimate = (left + right) / 2f;
            Assert.Equal(estimate, curve.SlopeAt(t), 2);
        }
    }

    [Fact]
    public void SlopeIsZeroOutsideTheKeysAndDuringAHold()
    {
        var curve = new TimingCurve(new[] { Key(0f, 0f), Key(2f, 1f), Key(4f, 1f), Key(6f, 2f) });
        Assert.Equal(0f, curve.SlopeAt(-1.0));
        Assert.Equal(0f, curve.SlopeAt(7.0));
        Assert.Equal(0f, curve.SlopeAt(3.0), 4);
    }

    [Fact]
    public void ANonFiniteTangentIsRejected()
    {
        var keys = new[] { Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, float.NaN, 0f), Key(1f, 1f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("Timing key 0 has a non-finite tangent.", ex.Message);
    }

    [Fact]
    public void AnUnbrokenKeyKeepsOneSlopeThroughANeighbouringLegsChange()
    {
        // Legs of 10 yalms in 2 s and in 4 s average 5 and 2.5 yalms/s. Ratios of 1.2 on both sides store slopes of 6 and 3;
        // unbroken, both sides take their average, 4.5, inside the monotone bound of 3 × 2.5 = 7.5.
        TimingKey Middle(bool broken) => Sided(2f, 10f, TangentMode.Manual, TangentMode.Manual, 1.2f, 1.2f, broken);
        var joined = new TimingCurve([Key(0f, 0f), Middle(false), Key(6f, 20f)]);
        var apart = new TimingCurve([Key(0f, 0f), Middle(true), Key(6f, 20f)]);

        Assert.Equal(4.5f, joined.SideSlope(1, KeySide.In), 1e-4f);
        Assert.Equal(4.5f, joined.SideSlope(1, KeySide.Out), 1e-4f);
        Assert.Equal(6f, apart.SideSlope(1, KeySide.In), 1e-4f);
        Assert.Equal(3f, apart.SideSlope(1, KeySide.Out), 1e-4f);
    }

    [Fact]
    public void AnUnbrokenKeysOneSlopeStaysWithinBothLegsMonotoneBound()
    {
        // Legs averaging 5 and 1 yalms/s (10 yalms in 2 s, then in 10 s). Ratios of 3 store 15 and 3, averaging 9; the slower
        // leg allows 3 × 1 = 3, so both sides take 3.
        var curve = new TimingCurve([
            Key(0f, 0f),
            Sided(2f, 10f, TangentMode.Manual, TangentMode.Manual, 3f, 3f),
            Key(12f, 20f),
        ]);

        Assert.Equal(3f, curve.SideSlope(1, KeySide.In), 1e-4f);
        Assert.Equal(3f, curve.SideSlope(1, KeySide.Out), 1e-4f);
    }

    [Fact]
    public void AnUnbrokenKeyBesideAHoldKeepsItsMovingSide()
    {
        // Key 1 arrives from a leg averaging 5 yalms/s and is followed by a hold (key 2 at the same place), so it has no
        // moving leg after it: its arrival stays 1.2 × 5 = 6, and the hold's own side is 0.
        var curve = new TimingCurve([
            Key(0f, 0f),
            Sided(2f, 10f, TangentMode.Manual, TangentMode.Manual, 1.2f, 1.2f),
            Key(4f, 10f),
            Key(6f, 20f),
        ]);

        Assert.Equal(6f, curve.SideSlope(1, KeySide.In), 1e-4f);
        Assert.Equal(0f, curve.SideSlope(1, KeySide.Out));
    }
}
