using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Camera;

public class WellFormedTests
{
    // WellFormedFrame's up (0, 1, 0) is unit length and square to its view direction -Z; its 1 rad field of
    // view is 57.3°, within the 5°-120° range.
    [Fact]
    public void AWellFormedFrameBreaksNoRule() => Assert.Null(WellFormed.FirstBroken(WellFormedFrame));

    // Each limit itself is allowed: a look-at exactly 0.01 yalms from a position at the origin, and a field of view of
    // exactly 5° or 120°.
    public static TheoryData<CameraState> AtTheLimits =>
        new()
        {
            new CameraState(Vector3.Zero, new Vector3(0f, 0f, -0.01f), Vector3.UnitY, 1f),
            WellFormedFrame with
            {
                Fov = EditLimits.MinFov,
            },
            WellFormedFrame with
            {
                Fov = EditLimits.MaxFov,
            },
        };

    [Theory]
    [MemberData(nameof(AtTheLimits))]
    public void AFrameAtALimitBreaksNoRule(CameraState frame) => Assert.Null(WellFormed.FirstBroken(frame));

    public static TheoryData<CameraState, string> Broken =>
        new()
        {
            { WellFormedFrame with { Position = new Vector3(float.NaN, 2f, 3f) }, "the position isn't finite" },
            {
                WellFormedFrame with
                {
                    LookAt = new Vector3(1f, float.PositiveInfinity, -7f),
                },
                "the look-at isn't finite"
            },
            { WellFormedFrame with { Up = new Vector3(0f, float.NaN, 0f) }, "the up isn't finite" },
            { WellFormedFrame with { Fov = float.NaN }, "the field of view isn't finite" },
            // 3 - 2.995 = 0.005 yalms, half the 1 cm minimum.
            {
                WellFormedFrame with
                {
                    LookAt = new Vector3(1f, 2f, 2.995f),
                },
                "the look-at is too close to the position"
            },
            // Length 1.01 is 0.01 from unit, ten times the 1e-3 allowed.
            { WellFormedFrame with { Up = new Vector3(0f, 1.01f, 0f) }, "the up isn't unit length" },
            // Up tipped 0.01 rad toward the view (-Z): (0, cos 0.01, -sin 0.01), whose dot with (0, 0, -1) is
            // sin 0.01 = 0.0099998, ten times the 1e-3 allowed; its length is still 1.
            {
                WellFormedFrame with
                {
                    Up = new Vector3(0f, 0.99995f, -0.0099998f),
                },
                "the up isn't square to the view"
            },
            // 4° = 0.0698 rad, below the 5° (0.0873 rad) minimum.
            { WellFormedFrame with { Fov = 0.0698f }, "the field of view is out of range" },
            // 121° = 2.1118 rad, above the 120° (2.0944 rad) maximum.
            { WellFormedFrame with { Fov = 2.1118f }, "the field of view is out of range" },
        };

    [Theory]
    [MemberData(nameof(Broken))]
    public void EachBrokenRuleIsNamed(CameraState frame, string rule) =>
        Assert.StartsWith($"{rule} (", WellFormed.FirstBroken(frame), StringComparison.Ordinal);
}
