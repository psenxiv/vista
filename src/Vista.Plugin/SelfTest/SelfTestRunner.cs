#if DEBUG
using Dalamud.Game.ClientState.Conditions;
using Vista.Core.Camera;
using Vista.Core.SelfTest;
using Vista.Core.Session;
using Vista.Core.Tracks.Playback;
using Vista.Plugin.Game;
using Vista.Plugin.Session;

namespace Vista.Plugin.SelfTest;

/// <summary>Runs /vista selftest: seven checks one after another across frames, each line to chat and the log.</summary>
internal sealed class SelfTestRunner(GameSession game, Func<IReadOnlyList<(string Name, bool Passed)>> touchPoints)
{
    /// <summary>How many frames a check waits for the camera hook before giving up.</summary>
    private const int HookWaitFrames = 30;

    /// <summary>How many frames the error handling check waits for Vista to stop.</summary>
    private const int StopWaitFrames = 10;

    private const string Reason = "self-test";

    private readonly List<SelfTestResult> results = [];
    private IEnumerator<int>? steps;
    private bool stopNotified;

    /// <summary>True from the start line until the end line.</summary>
    public bool Running => steps is not null;

    /// <summary>Starts a run, or says in chat and the log why it can't.</summary>
    public void Start()
    {
        var refusal = SelfTestRules.Refusal(
            Running,
            Plugin.ClientState.IsLoggedIn,
            Plugin.Condition[ConditionFlag.InCombat],
            Plugin.BetweenAreas,
            game.State.Mode,
            game.State.Stopped
        );
        if (refusal is not null)
        {
            Say(SelfTestReport.Line(refusal));
            return;
        }

        results.Clear();
        stopNotified = false;
        game.SelfTestRunning = true;
        Say(SelfTestReport.StartLine(Plugin.Build, DateTime.Now));
        steps = Run().GetEnumerator();
    }

    /// <summary>True while running, saying in chat and the log that a mode change must wait.</summary>
    public bool Refuses()
    {
        if (Running)
            Say(SelfTestReport.Line(SelfTestRules.Running));
        return Running;
    }

    /// <summary>Takes the run's next step. Call once a frame from the framework update.</summary>
    public void Tick()
    {
        if (steps is null)
            return;
        try
        {
            if (steps.MoveNext())
                return;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[selftest] the run threw");
            Report(SelfTestResult.Fail("run", $"threw {ex.GetType().Name}: {ex.Message}"));
        }

        Finish();
    }

    /// <summary>Notes that Vista told the player it stopped, for the error handling check.</summary>
    public void NoteStopNotified() => stopNotified = true;

    /// <summary>The checks in order, each yielding once per frame it waits; error handling last, since it leaves Vista stopped.</summary>
    private IEnumerable<int> Run()
    {
        Report(SelfTestRules.TouchPoints(touchPoints()));
        IEnumerable<int>[] checks =
        [
            RoundTrip(),
            MovementLock(),
            GameUiCheck(),
            InputHooks(),
            DryRun(),
            ErrorHandling(),
        ];
        foreach (var check in checks)
        {
            if (game.State.Stopped)
            {
                Report(SelfTestResult.Fail("run", $"Vista stopped before the checks ended: {game.State.StopReason}"));
                yield break;
            }

            foreach (var frame in check)
                yield return frame;
        }
    }

    /// <summary>Check 2: writes three known frames through the hook, reads each back before the game's next update, then hands the camera back.</summary>
    private IEnumerable<int> RoundTrip()
    {
        if (CameraAccess.ReadState() is not { } before)
        {
            Report(SelfTestResult.Fail("camera round trip", "the camera couldn't be read"));
            yield break;
        }

        var frames = SelfTestRules.RoundTripFrames(before);
        var waiting = new Queue<CameraState>(frames);
        var readBacks = new List<(CameraState Written, CameraState Read)>();
        CameraState? written = null;
        game.SelfTestHold();
        Plugin.Camera.SelfTestProbe(
            () => written = waiting.TryDequeue(out var next) ? next : null,
            () =>
            {
                if (written is { } wrote && ReadBack() is { } read)
                    readBacks.Add((wrote, read));
                written = null;
            }
        );
        for (var i = 0; i < HookWaitFrames && readBacks.Count < frames.Count; i++)
            yield return i;

        Plugin.Camera.SelfTestProbe(null, null);
        game.Release(Reason);
        yield return 0;
        yield return 0;
        Report(SelfTestRules.CameraRoundTrip(readBacks, frames.Count, before, CameraAccess.ReadState()));
    }

    /// <summary>Check 3: holds the movement lock for a frame, then lets it go.</summary>
    private IEnumerable<int> MovementLock()
    {
        var start = Plugin.Movement.Count;
        Plugin.Movement.Hold();
        yield return 0;
        var held = Plugin.Movement.Count;
        Plugin.Movement.Release();
        yield return 0;
        Report(SelfTestRules.MovementLock(start, held, Plugin.Movement.Count));
    }

    /// <summary>Check 4: hides the game UI and restores it, reading each back a frame later.</summary>
    private IEnumerable<int> GameUiCheck()
    {
        var before = GameUi.SelfTestVisible;
        if (before != true)
        {
            Report(SelfTestRules.GameUi(before, null, null));
            yield break;
        }

        GameUi.Hide();
        yield return 0;
        var whileHidden = GameUi.SelfTestVisible;
        GameUi.Restore();
        yield return 0;
        Report(SelfTestRules.GameUi(before, whileHidden, GameUi.SelfTestVisible));
    }

    /// <summary>Check 5: holds the camera for a frame so the input hooks sync on, then releases so they go off.</summary>
    private IEnumerable<int> InputHooks()
    {
        game.SelfTestHold();
        yield return 0;
        var (whileHeld, total) = Plugin.Input.SelfTestHookState();
        game.Release(Reason);
        yield return 0;
        Report(SelfTestRules.InputHooks(whileHeld, Plugin.Input.SelfTestHookState().Enabled, total));
    }

    /// <summary>Check 6: plays the open scene's playlist once through the Director and the camera hook, checking every frame.</summary>
    private IEnumerable<int> DryRun()
    {
        if (SelfTestDryRun.Shot(game.State.PlaylistItems()) is not { } shot)
        {
            Report(SelfTestDryRun.Skipped);
            yield break;
        }

        var run = new SelfTestDryRun(game.State.Scene);
        var director = new Director(game.Characters);
        director.GoLive(shot);
        (Guid Entry, double Time, CameraState Frame)? written = null;
        var ended = false;
        var hookCalls = 0;
        game.SelfTestHold();
        Plugin.Camera.SelfTestProbe(
            () =>
            {
                if (ended)
                    return null;
                var frame = director.Tick((float)Plugin.Framework.UpdateDelta.TotalSeconds * SelfTestDryRun.Speed);
                ended = director.IsFinished;
                written = frame is { } f ? (director.Playlist!.EntryId, director.ShotTime, f) : null;
                return frame;
            },
            () =>
            {
                hookCalls++;
                if (written is { } wrote && ReadBack() is { } read)
                    run.Check(wrote.Entry, wrote.Time, wrote.Frame, read);
                written = null;
            }
        );

        var idle = 0;
        var seen = 0;
        while (!(ended && written is null) && idle < HookWaitFrames)
        {
            idle = hookCalls == seen ? idle + 1 : 0;
            seen = hookCalls;
            yield return 0;
        }

        Plugin.Camera.SelfTestProbe(null, null);
        game.Release(Reason);
        Report(run.Result(finished: ended && written is null));
    }

    /// <summary>Check 7: holds everything as Live does, then makes the camera hook's work throw once and watches Vista stop.</summary>
    private IEnumerable<int> ErrorHandling()
    {
        var uiBefore = GameUi.SelfTestVisible;
        var counterBefore = Plugin.Movement.Count;
        game.SelfTestHold();
        if (uiBefore == true)
            GameUi.Hide();
        yield return 0;

        Plugin.Camera.SelfTestThrowOnce();
        for (var i = 0; i < StopWaitFrames && !game.State.Stopped; i++)
            yield return i;
        yield return 0;

        var after = new SelfTestAfterFault(
            game.State.StopReason,
            game.State.Mode,
            game.OwnsCamera,
            counterBefore,
            Plugin.Movement.Count,
            uiBefore,
            GameUi.SelfTestVisible,
            Plugin.Input.SelfTestHookState().Enabled,
            stopNotified
        );
        Report(SelfTestRules.ErrorHandling(after, SessionState.FaultReason(CameraController.Name)));
    }

    /// <summary>Hands everything back as a normal release does and writes the end line, whatever the run did.</summary>
    private void Finish()
    {
        steps?.Dispose();
        steps = null;
        Faults.Attempt("clearing the self-test probe", () => Plugin.Camera.SelfTestProbe(null, null));
        Faults.Attempt("releasing after the self-test", () => game.Release(Reason));
        Faults.Attempt("releasing the movement lock after the self-test", Plugin.Movement.Release);
        Faults.Attempt("restoring the game UI after the self-test", GameUi.Restore);
        game.SelfTestRunning = false;
        Say(SelfTestReport.EndLine(results, game.State.Stopped));
    }

    /// <summary>The camera as the hook last left it, field for field, with no rounding.</summary>
    private static CameraState? ReadBack() =>
        CameraAccess.Capture() is { } raw ? new CameraState(raw.Position, raw.LookAt, raw.Up, raw.Fov) : null;

    private void Report(SelfTestResult result)
    {
        results.Add(result);
        Say(result.Line);
    }

    private static void Say(string line)
    {
        Plugin.Log.Information("{Line:l}", line);
        Plugin.ChatGui.Print(line);
    }
}
#endif
