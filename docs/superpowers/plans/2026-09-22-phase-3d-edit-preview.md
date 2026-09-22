# Phase 3.d Edit Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play and Restart in Edit mode preview the edited track in place (mode stays Edit, UI visible, overlay hidden). Any edit, a flight key, a scrub or Pause stops the preview and hands the free-cam the frame shown.

**Architecture:** Two sequential tasks:
- **Task 1 (Core):** `SessionState` gains a preview `TrackPlayback`. Play and Restart in Edit mode start it, going live splits out into `GoLive`, and every edit path stops it. Existing tests that went live with Play or Restart from Edit move to `Cue` then `Play`.
- **Task 2 (Plugin):** `CameraSession` advances the preview and hands back to the free-cam when it stops. The free-cam reports flight keys, the fly-speed wheel stops a preview, the editor layer hides while previewing, and the transport icons follow the preview.

They're sequential because Task 2 needs Task 1's API.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-edit-preview-design.md`.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Check `git status` after committing. Do not push.
- A preview never records an undo step and never hides the game UI.

## Technical rulings (the cost if wrong is in brackets)

- **Going live splits out as a private `GoLive()`,** the old `Restart` body. `Cue` and `Play` from Off call it. `Play` and `Restart` from Edit preview instead. [If wrong: a different entry point for going live from Edit.]
- **Existing tests that went live with `Play()` or `Restart()` from Edit become `Cue()` then `Play()`,** which plays from the start exactly as before. Only the tests' way of going live changes; what they check stays the same. [If wrong: none; test plumbing.]
- **"Where the shot finishes" is decided by the playback itself:** seek a fresh playback to the scrub head, and if that finishes it, restart it. This covers every direction and Loop setting without special cases. [If wrong: special-case positions.]
- **Scrubbing in Edit mode stops a preview** (`BeginScrub` and `ScrubTo`), so a double-click jump to a point also stops it. [If wrong: none.]
- **The plugin notices a stopped preview by comparing it to the last frame** (was previewing, now isn't) and hands the free-cam that frame. Every stop path, including Core-side stops caused by edits, then behaves the same. [If wrong: explicit calls at each stop site.]
- **Selecting a point, key or leg doesn't stop a preview;** it isn't an edit. Markers can't be clicked anyway while the overlay is hidden. [If wrong: add `EndPreview` to `Select`.]

---

### Task 1: The preview in the session

**Files:**
- Modify: `src/Vista.Core/Session/SessionState.cs`
- Test: `tests/Vista.Tests/Session/SessionPreviewTests.cs` (new); existing session tests that go live from Edit

**Interfaces:**
- Produces:
  - `PlayOutcome` gains `Previewed`.
  - `SessionState.Previewing` (`bool`).
  - `CameraState? AdvancePreview(float dt)`: advances the preview, stops it at the end of a cycle that doesn't loop, and returns the frame (null when not previewing).
  - `bool StopPreview()`: stops a preview, leaving the scrub head at its shot time; false when there was none.
  - `Play()` and `Restart()` in Edit mode return `Previewed` or `Refused`. `Stop()` in Edit mode stops a preview. `Cue()` still goes live.

- [ ] **Step 1: Write the failing tests**

Create `tests/Vista.Tests/Session/SessionPreviewTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionPreviewTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing; three points at x = 0, 10, 20 at 2 yalms per second: a 10 s shot.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    [Fact]
    public void PlayInEditPreviewsFromTheScrubHeadAndStaysInEdit()
    {
        var state = Editing();
        state.ScrubTo(4.0);

        Assert.Equal(PlayOutcome.Previewed, state.Play());

        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.True(state.Previewing);
        Assert.Equal(4.0, state.ScrubHead, 5);
        state.AdvancePreview(1f);
        Assert.Equal(5.0, state.ScrubHead, 5);
        Assert.False(state.Director.IsLive);
    }

    [Theory]
    [InlineData(PlaybackDirection.Forward, 10.0, 0.0)]
    [InlineData(PlaybackDirection.Reverse, 0.0, 10.0)]
    public void PlayFromWhereTheShotFinishesStartsFromTheBeginning(PlaybackDirection direction, double finish, double start)
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetDirection(t, direction));
        state.ScrubTo(finish);

        state.Play();

        Assert.Equal(start, state.ScrubHead, 5);
    }

    [Fact]
    public void RestartInEditPreviewsFromTheBeginning()
    {
        var state = Editing();
        state.ScrubTo(6.0);

        Assert.Equal(PlayOutcome.Previewed, state.Restart());

        Assert.Equal(0.0, state.ScrubHead, 5);
        Assert.True(state.Previewing);
    }

    [Fact]
    public void StopKeepsTheScrubHeadWhereThePreviewWas()
    {
        var state = Editing();
        state.Play();
        state.AdvancePreview(3f);

        Assert.True(state.Stop());

        Assert.False(state.Previewing);
        Assert.Equal(3.0, state.ScrubHead, 5);
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void ATrackThatDoesNotLoopStopsAtTheEndAndALoopingOneCarriesOn()
    {
        var state = Editing();
        state.Play();
        Assert.NotNull(state.AdvancePreview(15f));
        Assert.False(state.Previewing);
        Assert.Equal(10.0, state.ScrubHead, 5);

        state.ChangeTrack(t => TrackEditing.SetLoop(t, true));
        state.Restart();
        state.AdvancePreview(15f);
        Assert.True(state.Previewing);
        Assert.Equal(5.0, state.ScrubHead, 3);
    }

    [Fact]
    public void EditsStopThePreview()
    {
        var state = Editing();

        state.Play();
        state.AddToEnd(Point(30f));
        Assert.False(state.Previewing);

        state.Play();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 1f));
        Assert.False(state.Previewing);

        state.Play();
        state.Undo();
        Assert.False(state.Previewing);

        state.Play();
        state.Redo();
        Assert.False(state.Previewing);

        state.Play();
        state.BeginLiveEdit();
        Assert.False(state.Previewing);
        state.EndLiveEdit();

        state.Play();
        state.AddTrack();
        Assert.False(state.Previewing);
    }

    [Fact]
    public void SwitchingTracksAndScrubbingStopThePreview()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);

        state.Play();
        state.SwitchTrack(state.Scene.Tracks[1].Id);
        Assert.False(state.Previewing);

        state.SwitchTrack(first);
        state.Play();
        state.BeginScrub();
        Assert.False(state.Previewing);
        state.EndScrub();

        state.Play();
        state.ScrubTo(2.0);
        Assert.False(state.Previewing);
    }

    [Fact]
    public void APreviewRecordsNoUndoStep()
    {
        var state = Editing();
        state.Play();
        state.AdvancePreview(15f);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void LeavingEditEndsThePreview()
    {
        var state = Editing();
        state.Play();

        state.Cue();
        Assert.False(state.Previewing);
        Assert.Equal(CameraMode.Live, state.Mode);

        state.Edit();
        state.Play();
        state.Release();
        Assert.False(state.Previewing);
    }

    [Fact]
    public void ATrackWithNoPointsIsRefused()
    {
        var state = new SessionState();
        state.Edit();
        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.False(state.Previewing);
    }

    [Fact]
    public void LiveIsUnchanged()
    {
        var state = Editing();
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.True(state.Director.IsLive);
        Assert.False(state.Previewing);
        Assert.Equal(PlayOutcome.Started, state.Restart());
    }

    [Fact]
    public void PlayFromOffStillGoesLive()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.Release();

        Assert.Equal(PlayOutcome.StartedFromOff, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter SessionPreviewTests`
Expected: the build fails because `Previewed`, `Previewing`, `AdvancePreview` and `StopPreview` don't exist.

- [ ] **Step 3: Implement the preview**

In `src/Vista.Core/Session/SessionState.cs`:

1. Add `Previewed` to `PlayOutcome`, and update its summary to mention the preview:

```csharp
/// <summary>What <see cref="SessionState.Play"/>, <see cref="SessionState.Restart"/> or <see cref="SessionState.Cue"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff, Cued, CuedFromOff, Previewed }
```

2. Add the field and the property:

```csharp
    private TrackPlayback? preview;

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => preview is not null;
```

3. Replace `Play`, `Restart`, `Cue` and `Stop` with:

```csharp
    /// <summary>In Edit, previews from the scrub head; live, resumes a paused shot or leaves a playing one alone; otherwise goes live.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Editing) return preview is not null ? PlayOutcome.Previewed : StartPreview(fromStart: false);
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused) return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return GoLive();
    }

    /// <summary>In Edit, previews from the beginning; otherwise goes live from the start. Refused with no points.</summary>
    public PlayOutcome Restart() => Mode == CameraMode.Editing ? StartPreview(fromStart: true) : GoLive();

    /// <summary>Goes live with the track paused at its start. Refused with no points.</summary>
    public PlayOutcome Cue()
    {
        var outcome = GoLive();
        if (outcome == PlayOutcome.Refused) return outcome;
        Director.Pause();
        return outcome == PlayOutcome.StartedFromOff ? PlayOutcome.CuedFromOff : PlayOutcome.Cued;
    }

    /// <summary>Live, holds the current frame; in Edit, stops a preview. Returns false when there was nothing to stop.</summary>
    public bool Stop()
    {
        if (Mode == CameraMode.Editing) return StopPreview();
        if (Mode != CameraMode.Live) return false;
        Director.Pause();
        return true;
    }

    /// <summary>Advances an Edit preview, stopping it at the end of a cycle that doesn't loop. Returns its frame, or null when not previewing.</summary>
    public CameraState? AdvancePreview(float dt)
    {
        if (preview is not { } playback) return null;
        var frame = playback.Advance(dt);
        if (playback.IsFinished) StopPreview();
        return frame;
    }

    /// <summary>Stops an Edit preview, leaving the scrub head at its shot time. Returns false if none was playing.</summary>
    public bool StopPreview()
    {
        if (preview is not { } playback) return false;
        scrubTime = playback.ShotTime;
        preview = null;
        return true;
    }

    /// <summary>Goes live with the track from its start. Refused with no points.</summary>
    private PlayOutcome GoLive()
    {
        if (Local.Points.Count == 0) return PlayOutcome.Refused;
        StopPreview();
        Scrubbing = false;
        EndLiveEdit();

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
    }

    /// <summary>Starts an Edit preview from the scrub head, or from the beginning when asked or when the scrub head is where the shot finishes.</summary>
    private PlayOutcome StartPreview(bool fromStart)
    {
        if (Local.Points.Count == 0) return PlayOutcome.Refused;
        EndLiveEdit();
        Scrubbing = false;

        var playback = new TrackPlayback(Track);
        if (!fromStart)
        {
            playback.Seek(ScrubHead);
            if (playback.IsFinished) playback.Restart();
        }

        preview = playback;
        return PlayOutcome.Previewed;
    }
```

4. `ScrubHead` reads the preview while one plays:

```csharp
    /// <summary>Seconds under the scrub head: shot time while live or previewing, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => Mode == CameraMode.Live ? Director.ShotTime : preview?.ShotTime ?? Math.Min(scrubTime, Duration);
```

5. Stop the preview at the start of each of these, before anything else they do: `CommitEdit`, `CommitScene`, `BeginLiveEdit`, `Undo`, `Redo`, `SwitchTrack`, `BeginScrub`, `ScrubTo`, `Release`, and `Edit` (harmless when already editing). Add `StopPreview();` as each method's first statement. In `ScrubTo`, call it only when `Mode == CameraMode.Editing`.

   `EndLiveEdit` must **not** stop the preview, since `StartPreview` calls it.

- [ ] **Step 4: Move existing tests that went live from Edit**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`

Tests that called `state.Play()` or `state.Restart()` while editing to go live now preview instead, so some existing tests fail. For each failing test whose intent is to be live, replace that call with `state.Cue(); state.Play();`. That goes live from the start and plays, exactly as `Play()` from Edit used to. Where a test checked the `PlayOutcome` returned by that call (`Started`), check it on `Cue()` (`Cued`) and `Play()` (`Resumed`) instead, only where the outcome was the point of the test. `grep -rn "\.Play()\|\.Restart()" tests` lists the candidates (about 23 calls in the session tests). Change no test's assertions beyond this. List every test you change in the report.

Run the suite again.
Expected: every test passes, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Session/SessionState.cs tests/Vista.Tests/Session/SessionPreviewTests.cs
git add <each existing test file you changed>
git commit -m "feat(session) preview the edited track in edit mode"
```

---

### Task 2: The preview in game

Starts once Task 1 is on `main`.

**Files:**
- Modify: `src/Vista.Plugin/Session/CameraSession.cs`, `src/Vista.Plugin/Game/FreeCam.cs`, `src/Vista.Plugin/Plugin.cs` (the fly-speed wheel), `src/Vista.Plugin/Editor/EditorLayer.cs`, `src/Vista.Plugin/Ui/TrackEditorWindow.cs` (transport)

**Interfaces:**
- Consumes: `SessionState.Previewing`, `AdvancePreview`, `StopPreview`, `PlayOutcome.Previewed` (Task 1).
- Produces: `CameraSession.Previewing`, `CameraSession.StopPreview()`, `FreeCam.HasFlightInput()`.

No Core changes, so no new tests; the build and the checklist cover it.

- [ ] **Step 1: The free-cam reports flight keys**

In `FreeCam.cs`, add:

```csharp
    /// <summary>True when a flight or roll key is held this frame, outside text fields.</summary>
    public static bool HasFlightInput() => !PhysicalKeys.IsTyping() && (ReadInput() != Vector3.Zero || ReadRoll() != 0f);
```

- [ ] **Step 2: `CameraSession` runs the preview**

In `CameraSession.cs`:

1. Add a field `private bool previewedLastFrame;` and:

```csharp
    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => state.Previewing;

    /// <summary>Stops an Edit preview; the free-cam takes over from the frame shown on the next frame.</summary>
    public void StopPreview() => state.StopPreview();
```

2. In `Frame`, the Editing case runs the preview first:

```csharp
            CameraMode.Editing => EditingFrame(dt),
```

   and add:

```csharp
    /// <summary>While editing: the preview's frame, the scrubbed frame, or the free-cam, handing the free-cam the last frame when a preview stops.</summary>
    private CameraState? EditingFrame(float dt)
    {
        if (state.Previewing && FreeCam.HasFlightInput()) state.StopPreview();
        var frame = state.AdvancePreview(dt);

        if (previewedLastFrame && !state.Previewing)
        {
            previewedLastFrame = false;
            if ((frame ?? state.FrameAt(state.ScrubHead)) is { } last) FlyFrom(last);
            return freeCam.Tick(dt);
        }

        previewedLastFrame = state.Previewing;
        if (frame is { } previewing) return previewing;
        return state.Scrubbing && state.FrameAt(state.ScrubHead) is { } scrubbed ? scrubbed : freeCam.Tick(dt);
    }
```

   A preview that stops for any reason (Pause, an edit, the end of the shot, a flight key) hands the free-cam its last frame, as a scrub release does.

   In `FlyFrom`, add `previewedLastFrame = false;` as its first line. An explicit fly that happens while stopping a preview, such as `OpenTrack` flying to an anchor (switching tracks stops the preview) or `JumpToPoint`, must not be overridden on the next frame by the hand-back to the preview's last frame.

3. In `Apply(PlayOutcome)`, handle `Previewed` before the other cases. It neither hides the UI nor disables the free-cam:

```csharp
            case PlayOutcome.Previewed:
                Plugin.Log.Information("[vista] preview");
                return;
```

4. `Stop()` logs "[vista] preview stopped" when it stopped a preview in Edit mode, and "[vista] paused" when live:

```csharp
    /// <summary>Live, holds the current frame; in Edit, stops a preview.</summary>
    public void Stop()
    {
        var editing = state.Mode == CameraMode.Editing;
        if (state.Stop()) Plugin.Log.Information(editing ? "[vista] preview stopped" : "[vista] paused");
    }
```

5. Update the doc comments of `Play`, `Restart` and `Cue` to match: in Edit, Play and Restart preview; Cue goes live.

- [ ] **Step 3: The fly-speed wheel stops a preview**

In `Plugin.cs` `OnDraw`, where the wheel steps the fly speed in Edit mode (`Session.Speed.Step(steps);`), stop a preview first:

```csharp
            if (steps != 0)
            {
                Session.StopPreview();
                Session.Speed.Step(steps);
                wheel -= steps;
            }
```

- [ ] **Step 4: Hide the editor layer while previewing**

In `EditorLayer.Draw`, treat a preview like not editing: the first line becomes

```csharp
        if (session.Mode != CameraMode.Editing || session.Previewing) { clicks.Reset(); gizmo.Cancel(); anchorGizmo.Cancel(session); return; }
```

(keeping whatever `Cancel` calls the line already makes).

- [ ] **Step 5: The transport follows the preview**

In `TrackEditorWindow.DrawTransport`:

```csharp
        var playing = session.Previewing || (session.Mode == CameraMode.Live && !session.Director.IsPaused && !session.Director.IsFinished);
```

and enable Restart in Edit mode as well as Live, when the track has points:

```csharp
        ImGui.BeginDisabled(session.Mode == CameraMode.Off || session.Track.Points.Count == 0);
```

Update `DrawTransport`'s and `DrawModeCombo`'s doc comments if they describe Play from Edit going live.

- [ ] **Step 6: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors. Run `dotnet test tests/Vista.Tests/Vista.Tests.csproj`: every test passes.

- [ ] **Step 7: Commit**

```bash
git add src/Vista.Plugin/Session/CameraSession.cs src/Vista.Plugin/Game/FreeCam.cs src/Vista.Plugin/Plugin.cs src/Vista.Plugin/Editor/EditorLayer.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs
git commit -m "feat(ui) preview in edit mode with the overlay hidden"
```

---

### After the tasks: the checklist

The controller writes a separate `CHECKLIST-3d.md` (untracked) after the final review, with falsifiable pass conditions, a Notes line each, and keys as words.
