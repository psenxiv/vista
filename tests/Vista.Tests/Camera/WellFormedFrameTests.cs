using System.Numerics;
using Vista.Core.Camera;
using Xunit;
using Xunit.Sdk;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Camera;

public class WellFormedFrameTests
{
    // At (1, 2, 3) looking 10 yalms along -Z with up +Y (unit, and square to -Z), field of view 1 rad (57.3°, within 5° to 120°).
    private static readonly CameraState Good = new(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, -7f),
        new Vector3(0f, 1f, 0f),
        1f
    );

    [Fact]
    public void AWellFormedFramePasses() => AssertWellFormed(Good, "good");

    public static TheoryData<string, CameraState, string> Broken =>
        new()
        {
            { "NaN position", Good with { Position = new Vector3(float.NaN, 2f, 3f) }, "the position isn't finite" },
            {
                "infinite look-at",
                Good with
                {
                    LookAt = new Vector3(1f, float.PositiveInfinity, -7f),
                },
                "the look-at isn't finite"
            },
            // 3 - 2.995 = 0.005 yalms, half the 1 cm minimum.
            {
                "look-at 0.5 cm away",
                Good with
                {
                    LookAt = new Vector3(1f, 2f, 2.995f),
                },
                "the look-at is too close to the position"
            },
            // Length 1.01 is 0.01 from unit, ten times the 1e-3 allowed.
            { "long up", Good with { Up = new Vector3(0f, 1.01f, 0f) }, "the up isn't unit length" },
            // Up tipped 0.01 rad toward the view (-Z): (0, cos 0.01, -sin 0.01), whose dot with (0, 0, -1) is
            // sin 0.01 = 0.0099998, ten times the 1e-3 allowed; its length is still 1.
            {
                "up tipped 0.01 rad",
                Good with
                {
                    Up = new Vector3(0f, 0.99995f, -0.0099998f),
                },
                "the up isn't square to the view"
            },
            // 4° = 0.0698 rad, below the 5° (0.0873 rad) minimum.
            { "narrow field of view", Good with { Fov = 0.0698f }, "the field of view is out of range" },
            // 121° = 2.1118 rad, above the 120° (2.0944 rad) maximum.
            { "wide field of view", Good with { Fov = 2.1118f }, "the field of view is out of range" },
        };

    [Theory]
    [MemberData(nameof(Broken))]
    public void EachBrokenRuleFailsNamingIt(string where, CameraState frame, string rule)
    {
        var failure = Assert.Throws<FailException>(() => AssertWellFormed(frame, where));

        Assert.StartsWith($"{where}: {rule} (", failure.Message);
    }
}
