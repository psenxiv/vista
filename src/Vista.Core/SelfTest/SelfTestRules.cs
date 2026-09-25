#if DEBUG
using System.Globalization;
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Session;

namespace Vista.Core.SelfTest;

/// <summary>Whether each self-test check passed, from the values the plugin read back from the game.</summary>
public static class SelfTestRules
{
    /// <summary>How far the camera may be from where it was once handed back, in yalms: 1 cm; provisional.</summary>
    public const float RestoreDistance = 0.01f;

    /// <summary>How far the camera's facing may be from where it was once handed back, in degrees; provisional.</summary>
    public const double RestoreDegrees = 0.1;

    /// <summary>Why a self-test can't start now, or null when it can.</summary>
    public static string? Refusal(
        bool running,
        bool loggedIn,
        bool inCombat,
        bool betweenAreas,
        CameraMode mode,
        bool stopped
    ) =>
        running ? "Self-test: running; wait for it to end."
        : !loggedIn ? "Self-test: log in first."
        : stopped ? "Self-test: Vista has stopped; reload it first."
        : inCombat ? "Self-test: leave combat first."
        : betweenAreas ? "Self-test: wait until you've arrived."
        : mode != CameraMode.Off ? "Self-test: go to Off first."
        : null;

    /// <summary>Check 1: passes when every touch point resolved and installed.</summary>
    public static SelfTestResult TouchPoints(IReadOnlyList<(string Name, bool Passed)> points)
    {
        var installed = $"{points.Count(p => p.Passed)} of {points.Count} installed";
        var unavailable = points.Where(p => !p.Passed).Select(p => p.Name).ToList();
        return unavailable.Count == 0
            ? SelfTestResult.Pass("touch points", installed)
            : SelfTestResult.Fail("touch points", $"{installed}; unavailable: {string.Join(", ", unavailable)}");
    }

    /// <summary>The frames the camera round trip writes: <paramref name="before"/> moved 1, 2 and 3 yalms up, facing the same way.</summary>
    public static IReadOnlyList<CameraState> RoundTripFrames(CameraState before) =>
        [
            .. Enumerable
                .Range(1, 3)
                .Select(up =>
                    before with
                    {
                        Position = before.Position + (Vector3.UnitY * up),
                        LookAt = before.LookAt + (Vector3.UnitY * up),
                    }
                ),
        ];

    /// <summary>The first part of <paramref name="read"/> that isn't bit for bit what was <paramref name="written"/>, with both values, or null when all of it is.</summary>
    public static string? ReadBackMismatch(CameraState written, CameraState read) =>
        Mismatch("position", written.Position, read.Position)
        ?? Mismatch("look-at", written.LookAt, read.LookAt)
        ?? Mismatch("up", written.Up, read.Up)
        ?? (
            Same(written.Fov, read.Fov)
                ? null
                : Invariant($"the field of view read back as {read.Fov:G9}, written {written.Fov:G9}")
        );

    /// <summary>Check 2: passes when the hook read back every one of <paramref name="frames"/> exactly as written, and the camera was handed back within 1 cm and 0.1° of <paramref name="before"/>.</summary>
    public static SelfTestResult CameraRoundTrip(
        IReadOnlyList<(CameraState Written, CameraState Read)> readBacks,
        int frames,
        CameraState before,
        CameraState? after
    )
    {
        var (readBackPassed, readBack) = ReadBacks(readBacks, frames);
        var (restorePassed, restore) = Restore(before, after);
        return SelfTestResult.Of("camera round trip", readBackPassed && restorePassed, $"{readBack}; {restore}");
    }

    /// <summary>Check 3: passes when holding raised the counter from <paramref name="start"/> by exactly one and releasing returned it.</summary>
    public static SelfTestResult MovementLock(int start, int held, int released)
    {
        const string check = "movement lock";
        if (held != start + 1)
            return SelfTestResult.Fail(check, $"counter {start} then {held}, expected {start + 1}");
        return released != start
            ? SelfTestResult.Fail(check, $"counter {start} then {held} then {released}, expected {start}")
            : SelfTestResult.Pass(check, $"counter {start} then {held} then {released}");
    }

    /// <summary>Check 4: passes when hiding made the game report its UI hidden and restoring made it shown again; skipped when it started hidden. Null is unreadable.</summary>
    public static SelfTestResult GameUi(bool? shownBefore, bool? shownWhileHidden, bool? shownAfter)
    {
        const string check = "game UI";
        if (shownBefore == false)
            return SelfTestResult.Skip(check, "the game UI was already hidden");
        var seen = $"{Shown(shownBefore)}, then {Shown(shownWhileHidden)}, then {Shown(shownAfter)}";
        return shownBefore == true && shownWhileHidden == false && shownAfter == true
            ? SelfTestResult.Pass(check, seen)
            : SelfTestResult.Fail(check, $"{seen}, expected shown, then hidden, then shown");
    }

    /// <summary>Check 5: passes when all <paramref name="total"/> input hooks were enabled while Vista held the camera, and none once it let go.</summary>
    public static SelfTestResult InputHooks(int enabledWhileHeld, int enabledAfterRelease, int total) =>
        SelfTestResult.Of(
            "input hooks",
            enabledWhileHeld == total && enabledAfterRelease == 0,
            $"{enabledWhileHeld} of {total} enabled while held, {enabledAfterRelease} after release"
        );

    /// <summary>Check 7: passes when the fault stopped Vista for <paramref name="expectedReason"/>, everything was handed back as it was, and the player was told.</summary>
    public static SelfTestResult ErrorHandling(SelfTestAfterFault after, string expectedReason)
    {
        const string check = "error handling";
        List<string> wrong = [];
        if (after.StopReason != expectedReason)
            wrong.Add($"stopped for {after.StopReason ?? "nothing"}, expected {expectedReason}");
        if (after.Mode != CameraMode.Off || after.OwnsCamera)
            wrong.Add($"left in {after.Mode}{(after.OwnsCamera ? " holding the camera" : "")}, expected Off");
        if (after.CounterAfter != after.CounterBefore)
            wrong.Add($"movement counter {after.CounterBefore} then {after.CounterAfter}");
        if (after.UiAfter != after.UiBefore)
            wrong.Add($"game UI {Shown(after.UiBefore)} then {Shown(after.UiAfter)}");
        if (after.HooksEnabled != 0)
            wrong.Add($"input hooks still enabled: {after.HooksEnabled}");
        if (!after.Notified)
            wrong.Add("no stop notification");
        return wrong.Count > 0
            ? SelfTestResult.Fail(check, string.Join("; ", wrong))
            : SelfTestResult.Pass(
                check,
                $"stopped for {after.StopReason}, released to Off, movement counter {after.CounterBefore} again, game UI {Shown(after.UiAfter)}, input hooks disabled, player told"
            );
    }

    /// <summary>Whether the read-backs covered every frame and each was exact, with the first that wasn't.</summary>
    private static (bool Passed, string Detail) ReadBacks(
        IReadOnlyList<(CameraState Written, CameraState Read)> readBacks,
        int frames
    )
    {
        if (readBacks.Count < frames)
            return (false, $"the hook read back {readBacks.Count} of {frames} frames");
        for (var i = 0; i < readBacks.Count; i++)
            if (ReadBackMismatch(readBacks[i].Written, readBacks[i].Read) is { } mismatch)
                return (false, $"frame {i + 1}: {mismatch}");
        return (true, $"{frames} frames read back exactly");
    }

    /// <summary>Whether the camera handed back is within the tolerances of <paramref name="before"/>, with how far it moved and turned.</summary>
    private static (bool Passed, string Detail) Restore(CameraState before, CameraState? after)
    {
        if (after is not { } handedBack)
            return (false, "the camera couldn't be read after release");
        var moved = Vector3.Distance(before.Position, handedBack.Position);
        var turned = Degrees(before.LookAt - before.Position, handedBack.LookAt - handedBack.Position);
        var passed = moved <= RestoreDistance && turned <= RestoreDegrees;
        var detail = Invariant($"handed back {moved * 100f:0.00} cm and {turned:0.000}° from where it was");
        return passed
            ? (true, detail)
            : (false, Invariant($"{detail}, expected within {RestoreDistance * 100f:0.##} cm and {RestoreDegrees}°"));
    }

    /// <summary>The angle between two directions in degrees.</summary>
    private static double Degrees(Vector3 from, Vector3 to) =>
        Math.Atan2(Vector3.Cross(from, to).Length(), Vector3.Dot(from, to)) * 180.0 / Math.PI;

    private static string? Mismatch(string part, Vector3 written, Vector3 read) =>
        Same(written.X, read.X) && Same(written.Y, read.Y) && Same(written.Z, read.Z)
            ? null
            : $"the {part} read back as {Show(read)}, written {Show(written)}";

    private static bool Same(float a, float b) =>
        BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static string Show(Vector3 v) => Invariant($"({v.X:G9}, {v.Y:G9}, {v.Z:G9})");

    private static string Shown(bool? shown) =>
        shown switch
        {
            true => "shown",
            false => "hidden",
            null => "unreadable",
        };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
#endif
