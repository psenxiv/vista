using System.Linq;
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class TimingCurveTests
{
    private static TimingKey Key(float time, float position, TangentMode mode = TangentMode.Auto, float inTangent = 0f, float outTangent = 0f)
        => new(time, position, mode, mode, inTangent, outTangent);

    private static TimingKey Sided(float time, float position, TangentMode inMode, TangentMode outMode, float inTangent = 0f, float outTangent = 0f)
        => new(time, position, inMode, outMode, inTangent, outTangent);

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
        Assert.Equal("timing keys must have strictly increasing times", ex.Message);
    }

    [Fact]
    public void ConstructorRejectsAnUnknownTangentMode()
    {
        var keys = new[] { Key(0f, 0f, (TangentMode)99), Key(1f, 1f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("timing key 0 has an unknown tangent mode", ex.Message);
    }

    [Fact]
    public void PositionAtKeepsFullPrecisionOnLongShots()
    {
        // Keys 0.0625 s apart, one float step at a million seconds. A time cast to float
        // rounds 1_000_000.04 up to the next key and evaluates the wrong interval.
        var keys = new[]
        {
            Key(0f, 0f),
            Key(1_000_000f, 1f),
            Key(1_000_000.0625f, 1f),
            Key(1_000_000.125f, 2f),
        };
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
        Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
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
    public void OneSidedEndsGiveFullSpeedStartAndDeadStopAtEnd()
    {
        var keys = new[] { Key(0f, 0f), Key(1f, 1f), Key(2f, 4f) };
        var curve = new TimingCurve(keys);

        const double eps = 1e-3;
        var speedAtStart = (curve.PositionAt(eps) - curve.PositionAt(0)) / eps;
        var speedAtEnd = (curve.PositionAt(2.0) - curve.PositionAt(2.0 - eps)) / eps;

        Assert.Equal(1.0, speedAtStart, 2);
        Assert.Equal(3.0, speedAtEnd, 2);
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
    public void StraightCurveGivesConstantWorldSpeedWithinASegmentOnUnevenlySpacedPoints()
    {
        // Same "bunched-then-spread" shape as ArcLengthTableTests: point 1 to point 2 is
        // the long segment whose spline parameter is not proportional to arc length.
        var points = new Vector3[]
        {
            new(0, 0, 0),
            new(0.001f, 0, 0),
            new(10, 0, 0),
            new(1000, 0, 0),
        };
        var table = new ArcLengthTable(points);

        // A straight, evenly-timed ramp through control-point units 1..2 (segment 1):
        // constant control-point-units-per-second, which arc-length evaluation should
        // turn into constant world-space-units-per-second within that segment.
        var keys = new[] { Key(0f, 0f), Key(1f, 1f), Key(2f, 2f), Key(3f, 3f) };
        var curve = new TimingCurve(keys);

        const int steps = 5;
        var worldPositions = new Vector3[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = 1.0 + (double)i / steps; // time in [1, 2], within segment 1
            var position = curve.PositionAt(t);
            var segment = (int)MathF.Floor(position);
            var fraction = position - segment;
            var parameter = table.ParameterAt(segment, fraction);
            worldPositions[i] = CatmullRom.Evaluate(points, segment, parameter);
        }

        var distances = new float[steps];
        for (var i = 0; i < steps; i++)
            distances[i] = Vector3.Distance(worldPositions[i], worldPositions[i + 1]);

        var mean = distances.Average();
        foreach (var d in distances)
            Assert.True(MathF.Abs(d - mean) < mean * 0.05f, $"world step {d} strays from mean {mean}");
    }

    [Fact]
    public void LinearSidesMakeEachSpanStraight()
    {
        var curve = new TimingCurve(new[]
        {
            Sided(0f, 0f, TangentMode.Linear, TangentMode.Linear),
            Sided(2f, 4f, TangentMode.Linear, TangentMode.Linear),
            Sided(4f, 5f, TangentMode.Linear, TangentMode.Linear),
        });

        Assert.Equal(2f, curve.PositionAt(1.0), 4);
        Assert.Equal(2f, curve.SlopeAt(1.0), 4);
        Assert.Equal(4.5f, curve.PositionAt(3.0), 4);
        Assert.Equal(0.5f, curve.SlopeAt(3.0), 4);
    }

    [Fact]
    public void AFlatInSideArrivesAtRestAndAFlatOutSideLeavesFromRest()
    {
        var curve = new TimingCurve(new[]
        {
            Sided(0f, 0f, TangentMode.Auto, TangentMode.Flat),
            Sided(2f, 4f, TangentMode.Flat, TangentMode.Auto),
        });

        Assert.Equal(0f, curve.SideSlope(0, KeySide.Out));
        Assert.Equal(0f, curve.SideSlope(1, KeySide.In));
        Assert.True(curve.SlopeAt(0.001) < 0.05f);
        Assert.True(curve.SlopeAt(1.999) < 0.05f);
        Assert.True(curve.SlopeAt(1.0) > 2f);
    }

    [Fact]
    public void EachSideOfAKeyFollowsItsOwnMode()
    {
        var curve = new TimingCurve(new[]
        {
            Sided(0f, 0f, TangentMode.Auto, TangentMode.Auto),
            Sided(2f, 4f, TangentMode.Flat, TangentMode.Linear),
            Sided(4f, 5f, TangentMode.Auto, TangentMode.Auto),
        });

        Assert.Equal(0f, curve.SideSlope(1, KeySide.In));
        Assert.Equal(0.5f, curve.SideSlope(1, KeySide.Out), 4);
    }

    [Fact]
    public void AManualSideUsesItsTangentWithinTheMonotoneLimit()
    {
        var gentle = new TimingCurve(new[]
        {
            Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 1f, 1f),
            Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
        });
        Assert.Equal(1f, gentle.SideSlope(0, KeySide.Out), 4);

        var steep = new TimingCurve(new[]
        {
            Sided(0f, 0f, TangentMode.Manual, TangentMode.Manual, 100f, 100f),
            Sided(2f, 4f, TangentMode.Auto, TangentMode.Auto),
        });
        Assert.Equal(6f, steep.SideSlope(0, KeySide.Out), 4);
    }

    [Fact]
    public void SlopeAtMatchesTheCurvesRateOfChange()
    {
        var curve = new TimingCurve(new[] { Key(0f, 0f), Key(2f, 4f), Key(5f, 5f), Key(6f, 9f) });
        foreach (var t in new[] { 0.5, 1.7, 3.2, 5.5 })
        {
            var estimate = (curve.PositionAt(t + 1e-3) - curve.PositionAt(t - 1e-3)) / 2e-3f;
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
        Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
    }
}
