#if DEBUG
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.SelfTest;
using Vista.Core.Session;
using Xunit;

namespace Vista.Tests.SelfTest;

public class SelfTestRulesTests
{
    // At (1, 2, 3) looking 10 yalms along -Z with up +Y and a 1 rad field of view.
    private static readonly CameraState Before = new(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, -7f),
        Vector3.UnitY,
        1f
    );

    public static TheoryData<bool, bool, bool, bool, CameraMode, bool, string?> Refusals =>
        new()
        {
            { false, true, false, false, CameraMode.Off, false, null },
            { true, true, false, false, CameraMode.Off, false, "Self-test: running; wait for it to end." },
            { false, false, false, false, CameraMode.Off, false, "Self-test: log in first." },
            { false, true, false, false, CameraMode.Off, true, "Self-test: Vista has stopped; reload it first." },
            { false, true, true, false, CameraMode.Off, false, "Self-test: leave combat first." },
            { false, true, false, true, CameraMode.Off, false, "Self-test: wait until you've arrived." },
            { false, true, false, false, CameraMode.View, false, "Self-test: go to Off first." },
            { false, true, false, false, CameraMode.Editing, false, "Self-test: go to Off first." },
            { false, true, false, false, CameraMode.Live, false, "Self-test: go to Off first." },
        };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ItRunsOnlyLoggedInOutOfCombatArrivedInOffAndNotStopped(
        bool running,
        bool loggedIn,
        bool inCombat,
        bool betweenAreas,
        CameraMode mode,
        bool stopped,
        string? refusal
    ) => Assert.Equal(refusal, SelfTestRules.Refusal(running, loggedIn, inCombat, betweenAreas, mode, stopped));

    [Fact]
    public void EveryTouchPointInstalledPasses() =>
        Assert.Equal(
            SelfTestResult.Pass("touch points", "4 of 4 installed"),
            SelfTestRules.TouchPoints([
                ("input query hooks", true),
                ("mouse wheel hook", true),
                ("movement lock", true),
                ("camera update hook", true),
            ])
        );

    [Fact]
    public void AnUnavailableTouchPointFailsNamingIt() =>
        Assert.Equal(
            SelfTestResult.Fail("touch points", "2 of 4 installed; unavailable: movement lock, camera update hook"),
            SelfTestRules.TouchPoints([
                ("input query hooks", true),
                ("mouse wheel hook", true),
                ("movement lock", false),
                ("camera update hook", false),
            ])
        );

    [Fact]
    public void TheRoundTripWritesTheCameraOneTwoAndThreeYalmsUpFacingTheSameWay() =>
        Assert.Equal(
            [
                Before with
                {
                    Position = new Vector3(1f, 3f, 3f),
                    LookAt = new Vector3(1f, 3f, -7f),
                },
                Before with
                {
                    Position = new Vector3(1f, 4f, 3f),
                    LookAt = new Vector3(1f, 4f, -7f),
                },
                Before with
                {
                    Position = new Vector3(1f, 5f, 3f),
                    LookAt = new Vector3(1f, 5f, -7f),
                },
            ],
            SelfTestRules.RoundTripFrames(Before)
        );

    [Fact]
    public void AnExactReadBackMatches() => Assert.Null(SelfTestRules.ReadBackMismatch(Before, Before));

    // Each read-back is one float step from what was written: 2.00000024 is 2 + 2^-22, the next float above 2;
    // -7.00000048 is 7 + 2^-21 below -7; 1.00000012 is 1 + 2^-23. -0 differs from 0 only in its sign bit.
    public static TheoryData<CameraState, string> Mismatches =>
        new()
        {
            {
                Before with
                {
                    Position = new Vector3(1f, 2.00000024f, 3f),
                },
                "the position read back as (1, 2.00000024, 3), written (1, 2, 3)"
            },
            {
                Before with
                {
                    LookAt = new Vector3(1f, 2f, -7.00000048f),
                },
                "the look-at read back as (1, 2, -7.00000048), written (1, 2, -7)"
            },
            { Before with { Up = new Vector3(-0f, 1f, 0f) }, "the up read back as (-0, 1, 0), written (0, 1, 0)" },
            { Before with { Fov = 1.00000012f }, "the field of view read back as 1.00000012, written 1" },
        };

    [Theory]
    [MemberData(nameof(Mismatches))]
    public void AReadBackOffByOneBitNamesThePartAndBothValues(CameraState read, string mismatch) =>
        Assert.Equal(mismatch, SelfTestRules.ReadBackMismatch(Before, read));

    // Position, look-at, up and field of view are compared in that order, and the first that differs is named.
    public static TheoryData<CameraState, string> FirstMismatches =>
        new()
        {
            {
                new CameraState(new Vector3(1f, 2.5f, 3f), new Vector3(1f, 2f, -6f), -Vector3.UnitY, 2f),
                "the position read back as (1, 2.5, 3), written (1, 2, 3)"
            },
            {
                Before with
                {
                    LookAt = new Vector3(1f, 2f, -6f),
                    Up = -Vector3.UnitY,
                    Fov = 2f,
                },
                "the look-at read back as (1, 2, -6), written (1, 2, -7)"
            },
            { Before with { Up = -Vector3.UnitY, Fov = 2f }, "the up read back as (-0, -1, -0), written (0, 1, 0)" },
        };

    [Theory]
    [MemberData(nameof(FirstMismatches))]
    public void TheFirstPartThatDiffersIsNamed(CameraState read, string mismatch) =>
        Assert.Equal(mismatch, SelfTestRules.ReadBackMismatch(Before, read));

    [Fact]
    public void ANotANumberReadBackWithTheSameBitsMatches()
    {
        // Bit for bit, NaN equals the same NaN; the well-formed rules are what reject it.
        var nan = Before with
        {
            Fov = float.NaN,
        };

        Assert.Null(SelfTestRules.ReadBackMismatch(nan, nan));
    }

    private static (CameraState, CameraState)[] Exact(IReadOnlyList<CameraState> frames) =>
        [.. frames.Select(f => (f, f))];

    [Fact]
    public void AnExactRoundTripHandedBackWherePasses() =>
        Assert.Equal(
            SelfTestResult.Pass(
                "camera round trip",
                "3 frames read back exactly; handed back 0.00 cm and 0.000° from where it was"
            ),
            SelfTestRules.CameraRoundTrip(Exact(SelfTestRules.RoundTripFrames(Before)), 3, Before, Before)
        );

    [Fact]
    public void AFrameTheHookNeverReadBackFails() =>
        Assert.Equal(
            SelfTestResult.Fail(
                "camera round trip",
                "the hook read back 2 of 3 frames; handed back 0.00 cm and 0.000° from where it was"
            ),
            SelfTestRules.CameraRoundTrip(Exact(SelfTestRules.RoundTripFrames(Before))[..2], 3, Before, Before)
        );

    [Fact]
    public void AFrameReadBackDifferentlyFailsNamingIt()
    {
        var readBacks = Exact(SelfTestRules.RoundTripFrames(Before));
        readBacks[1] = (readBacks[1].Item1, readBacks[1].Item1 with { Fov = 1.00000012f });

        Assert.Equal(
            SelfTestResult.Fail(
                "camera round trip",
                "frame 2: the field of view read back as 1.00000012, written 1; handed back 0.00 cm and 0.000° from where it was"
            ),
            SelfTestRules.CameraRoundTrip(readBacks, 3, Before, Before)
        );
    }

    [Fact]
    public void ACameraHandedBackTwoCentimetresAwayFails() =>
        // Moved 1.02 - 1 = 0.02 yalms (2 cm) along X, looking the same way; the limit is 1 cm.
        Assert.Equal(
            SelfTestResult.Fail(
                "camera round trip",
                "3 frames read back exactly; handed back 2.00 cm and 0.000° from where it was, expected within 1 cm and 0.1°"
            ),
            SelfTestRules.CameraRoundTrip(
                Exact(SelfTestRules.RoundTripFrames(Before)),
                3,
                Before,
                Before with
                {
                    Position = new Vector3(1.02f, 2f, 3f),
                    LookAt = new Vector3(1.02f, 2f, -7f),
                }
            )
        );

    [Fact]
    public void ACameraHandedBackTurnedFails() =>
        // The look-at raised 0.0349067 over 10 yalms turns the view by atan(0.00349067) = 0.2°; the limit is 0.1°.
        Assert.Equal(
            SelfTestResult.Fail(
                "camera round trip",
                "3 frames read back exactly; handed back 0.00 cm and 0.200° from where it was, expected within 1 cm and 0.1°"
            ),
            SelfTestRules.CameraRoundTrip(
                Exact(SelfTestRules.RoundTripFrames(Before)),
                3,
                Before,
                Before with
                {
                    LookAt = new Vector3(1f, 2.0349067f, -7f),
                }
            )
        );

    [Fact]
    public void ACameraHandedBackJustWithinTheLimitsPasses() =>
        // Moved 0.009 yalms (0.9 cm) along X; the look-at raised 0.0157 over 10 yalms turns by atan(0.00157) = 0.090°.
        Assert.Equal(
            SelfTestResult.Pass(
                "camera round trip",
                "3 frames read back exactly; handed back 0.90 cm and 0.090° from where it was"
            ),
            SelfTestRules.CameraRoundTrip(
                Exact(SelfTestRules.RoundTripFrames(Before)),
                3,
                Before,
                Before with
                {
                    Position = new Vector3(1.009f, 2f, 3f),
                    LookAt = new Vector3(1.009f, 2.0157f, -7f),
                }
            )
        );

    [Fact]
    public void ACameraHandedBackExactlyOneCentimetreAwayPasses()
    {
        // At the origin looking 10 yalms along -Z, then moved 0.01 yalms along X: exactly the 1 cm limit.
        var origin = new CameraState(Vector3.Zero, new Vector3(0f, 0f, -10f), Vector3.UnitY, 1f);
        var moved = origin with { Position = new Vector3(0.01f, 0f, 0f), LookAt = new Vector3(0.01f, 0f, -10f) };

        Assert.Equal(
            SelfTestResult.Pass(
                "camera round trip",
                "3 frames read back exactly; handed back 1.00 cm and 0.000° from where it was"
            ),
            SelfTestRules.CameraRoundTrip(Exact(SelfTestRules.RoundTripFrames(origin)), 3, origin, moved)
        );
    }

    [Fact]
    public void ACameraUnreadableAfterReleaseFails() =>
        Assert.Equal(
            SelfTestResult.Fail(
                "camera round trip",
                "3 frames read back exactly; the camera couldn't be read after release"
            ),
            SelfTestRules.CameraRoundTrip(Exact(SelfTestRules.RoundTripFrames(Before)), 3, Before, null)
        );

    [Theory]
    [InlineData(2, 3, 2, true, "counter 2 then 3 then 2")]
    [InlineData(0, 1, 0, true, "counter 0 then 1 then 0")]
    [InlineData(2, 2, 2, false, "counter 2 then 2, expected 3")]
    [InlineData(2, 4, 2, false, "counter 2 then 4, expected 3")]
    [InlineData(2, 3, 3, false, "counter 2 then 3 then 3, expected 2")]
    public void TheMovementLockRaisesTheCounterByOneAndPutsItBack(
        int start,
        int held,
        int released,
        bool passed,
        string detail
    ) =>
        Assert.Equal(
            SelfTestResult.Of("movement lock", passed, detail),
            SelfTestRules.MovementLock(start, held, released)
        );

    [Fact]
    public void AGameUiAlreadyHiddenIsSkipped() =>
        Assert.Equal(
            SelfTestResult.Skip("game UI", "the game UI was already hidden"),
            SelfTestRules.GameUi(false, false, false)
        );

    [Theory]
    [InlineData(true, false, true, true, "shown, then hidden, then shown")]
    [InlineData(true, true, true, false, "shown, then shown, then shown, expected shown, then hidden, then shown")]
    [InlineData(true, false, false, false, "shown, then hidden, then hidden, expected shown, then hidden, then shown")]
    [InlineData(
        null,
        null,
        null,
        false,
        "unreadable, then unreadable, then unreadable, expected shown, then hidden, then shown"
    )]
    [InlineData(
        true,
        false,
        null,
        false,
        "shown, then hidden, then unreadable, expected shown, then hidden, then shown"
    )]
    public void TheGameUiHidesAndComesBack(bool? before, bool? whileHidden, bool? after, bool passed, string detail) =>
        Assert.Equal(SelfTestResult.Of("game UI", passed, detail), SelfTestRules.GameUi(before, whileHidden, after));

    [Theory]
    [InlineData(5, 0, true, "5 of 5 enabled while held, 0 after release")]
    [InlineData(4, 0, false, "4 of 5 enabled while held, 0 after release")]
    [InlineData(5, 1, false, "5 of 5 enabled while held, 1 after release")]
    public void EveryInputHookIsOnWhileHeldAndOffAfter(int whileHeld, int after, bool passed, string detail) =>
        Assert.Equal(SelfTestResult.Of("input hooks", passed, detail), SelfTestRules.InputHooks(whileHeld, after, 5));

    private const string Reason = "fault in camera update hook";

    // Stopped for the fault, back in Off, the counter at 1 again, the UI shown as before, no hook on, the player told.
    private static readonly SelfTestAfterFault HandedBack = new(
        Reason,
        CameraMode.Off,
        false,
        1,
        1,
        true,
        true,
        0,
        true
    );

    [Fact]
    public void AFaultThatStopsVistaAndHandsEverythingBackPasses() =>
        Assert.Equal(
            SelfTestResult.Pass(
                "error handling",
                "stopped for fault in camera update hook, released to Off, movement counter 1 again, game UI shown, input hooks disabled, player told"
            ),
            SelfTestRules.ErrorHandling(HandedBack, Reason)
        );

    public static TheoryData<SelfTestAfterFault, string> Broken =>
        new()
        {
            { HandedBack with { StopReason = null }, "stopped for nothing, expected fault in camera update hook" },
            {
                HandedBack with
                {
                    StopReason = "fault in drawing",
                },
                "stopped for fault in drawing, expected fault in camera update hook"
            },
            { HandedBack with { Mode = CameraMode.Live }, "left in Live, expected Off" },
            { HandedBack with { OwnsCamera = true }, "left in Off holding the camera, expected Off" },
            { HandedBack with { CounterAfter = 2 }, "movement counter 1 then 2" },
            { HandedBack with { UiAfter = false }, "game UI shown then hidden" },
            { HandedBack with { HooksEnabled = 5 }, "input hooks still enabled: 5" },
            { HandedBack with { Notified = false }, "no stop notification" },
            {
                HandedBack with
                {
                    HooksEnabled = 1,
                    Notified = false,
                },
                "input hooks still enabled: 1; no stop notification"
            },
        };

    [Theory]
    [MemberData(nameof(Broken))]
    public void AnythingNotHandedBackFailsNamingIt(SelfTestAfterFault after, string detail) =>
        Assert.Equal(SelfTestResult.Fail("error handling", detail), SelfTestRules.ErrorHandling(after, Reason));
}
#endif
