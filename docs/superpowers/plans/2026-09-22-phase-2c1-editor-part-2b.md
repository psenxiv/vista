# Phase 2c-1 Editor, Part 2b: Windows, Scrub and Jumps

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the test window with the real editor UI: a track editor window and a Point window, a scrub bar and point jumps that keep the frame's aim, and remove the probes.

**Architecture:** Core gains the pure parts, built test-first: a point's arrival time, the ranges number fields clamp to, and scrub state in `SessionState` (the scrub head, and holding then resuming live playback). `CameraSession` shows scrubbed frames while editing and puts the free-cam at a frame, writing `DirH`/`DirV` for its aim, on a scrub release, a point jump or Edit from Live. Two Dalamud windows share one `PendingField`, which applies a typed number when its field loses focus, when a window closes, and before any mode button acts.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), `Dalamud.Bindings.ImGui`, `Dalamud.Interface` (windowing, `ImRaii`, FontAwesome), FFXIVClientStructs (bundled commit `b53cdf38`).

**Spec:** `docs/superpowers/specs/2026-09-21-editor-design.md` (editor), within `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` (main). Citations name the editor spec's sections, e.g. *editor § Windows*.

## Global Constraints

- `CinematicCam.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`.
- `tests/CinematicCam.Tests` references Core only.
- Namespaces match folders. **Never create a `CinematicCam.Plugin.Camera` namespace**, because it shadows FFXIVClientStructs' `Camera`.
- Build the plugin with `./build.sh`, never bare `dotnet build`. Keep the build at 0 warnings. Tests: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`.
- Never write `Camera.Distance` or `InterpDistance`. The only game fields this plan writes are `DirH`/`DirV` (through `CameraAccess.WriteAngles`, proven runtime state by probe 2) and what `CameraAccess.WriteState` already writes. It reads `Camera.MinFoV`/`MaxFoV`.
- Nullable values passed straight into `Log.*(..., params object[])` raise CS8604. Format them first.
- Doc comments are one line, stating what a thing is or does. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no trailing period, **no body, no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Work and commit on `main`. Stage only your own files, never `git add -A`. Do not push.
- BDTHPlugin and Cammy have no licence: read them as reference only, never copy code.
- Do not add features, options or behaviour this plan does not name. If something needs a decision, stop and report it; do not settle it yourself.
- If the compiler rejects an ImGui call, find the matching overload under `~/code/Dalamud/imgui/Dalamud.Bindings.ImGui` (the `Custom/` folder has the `ImU8String` overloads) and report the substitution.
- **Invalid input is prevented, not reported** (*editor § Windows*). Buttons and menu items that cannot act are disabled. Number fields clamp through `EditLimits`. There is no error line. Anything still refused is logged with `Plugin.Log.Warning`.
- UI text names keys in words: "Backtick", "Alt + Backtick", "Ctrl + Backtick".
- The in-game checks at the end of each plugin task are collected into one `CHECKLIST.md` by the controller after the last task. Implementers do not run them.

## Decisions this plan relies on

Made by the user and recorded in the spec on 2026-09-21:
- Invalid input is prevented rather than reported, with these ranges: leg 0.1–600 s, hold 0–600 s, pitch ±89°, yaw and roll wrapped to ±180°, FoV the game's own `MinFoV`–`MaxFoV`, and X/Y/Z any finite value.
- The Add menu writes shortcuts as words.
- Rows are numbered from 1. Fields apply on close and before mode buttons.
- The Point window has no close button. Yaw and Pitch are disabled in Direction-of-travel mode.

Technical rulings made while planning (the cost if wrong is in brackets):
- **One `PendingField` for both windows.** Only one field can have focus at a time, so a single shared instance lets the track editor's mode buttons also apply an edit left in the Point window. [If wrong: none; each window could own one instead.]
- **The free-cam holds its own FoV,** set by Edit, scrub release and jumps. Before this it re-read the game's FoV, which was only ever the value we last wrote. [If wrong: FoV stops following anything that changes the game's FoV while editing; nothing does today.]
- **A point jump targets the point's arrival time,** its first timing key. The pose is the same for the whole hold. [If wrong: none.]
- **Aim written on a jump is clamped to the game's pitch limits** (`DirVMin`/`DirVMax`, about −85° to +45°). A point stored steeper than that shows at the limit after a jump. This is the same limit the free-cam already has. [If wrong: steep points need the free-cam to own its orientation, which the user declined on 2026-09-21.]
- **Closing the track editor mid-scrub ends the scrub.** [If wrong: none.]

## File map

| File | Responsibility |
|---|---|
| `src/CinematicCam.Core/Tracks/TrackEditing.cs` | + `PointSeconds` |
| `src/CinematicCam.Core/Editing/EditLimits.cs` | new: the ranges number fields clamp to |
| `src/CinematicCam.Core/Session/SessionState.cs` | + scrub head, begin/to/end scrub; mode changes end a scrub |
| `src/CinematicCam.Plugin/Game/FreeCam.cs` | holds its own FoV |
| `src/CinematicCam.Plugin/Game/CameraAccess.cs` | + `ReadFovLimits` |
| `src/CinematicCam.Plugin/Session/CameraSession.cs` | scrub and jump pass-throughs, `FlyFrom`, Edit from Live keeps aim; drop `LastFrame` |
| `src/CinematicCam.Plugin/Ui/PendingField.cs` | new: a number field that applies when it loses focus |
| `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs` | new: the track editor |
| `src/CinematicCam.Plugin/Ui/PointWindow.cs` | new: the Point window |
| `src/CinematicCam.Plugin/Editor/PointGizmo.cs` | + `SetMode`; `Mode` becomes read-only outside |
| `src/CinematicCam.Plugin/Ui/TestWindow.cs` | deleted |
| `src/CinematicCam.Plugin/Probes/*` | deleted |
| `src/CinematicCam.Plugin/Plugin.cs` | wires the two windows, `/ccam` opens the track editor, probes removed |

---

### Task 1: Point times and field limits in Core

*editor § Windows: the field ranges. editor § Scrub and jumps: double-click a row to jump to the point.*

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackEditing.cs`
- Create: `src/CinematicCam.Core/Editing/EditLimits.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`
- Test: `tests/CinematicCam.Tests/Editing/EditLimitsTests.cs`

**Interfaces:**
- Produces: `public static float TrackEditing.PointSeconds(Track track, int index)`, which is point `index`'s first key time and throws `ArgumentOutOfRangeException` for a bad index.
- Produces: `public static class EditLimits` with `MinLegSeconds = 0.1f`, `MaxSeconds = 600f`, and:
  - `float Leg(float seconds)`, `float Hold(float seconds)`
  - `float Pitch(float radians)`, `float Angle(float radians)`
  - `float Fov(float radians, float min, float max)`
  - `float Coordinate(float value, float current)`

- [ ] **Step 1: Write the failing tests**

Add to `TrackEditingTests` (it has `Build3PointTrack()`: points at x = 0, 10, 20 with keys at 0, 5 and 10 s):

```csharp
    [Fact]
    public void PointSecondsIsWhenThePointIsReached()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.PointSeconds(track, 0));
        Assert.Equal(5f, TrackEditing.PointSeconds(track, 1));
        Assert.Equal(12f, TrackEditing.PointSeconds(track, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.PointSeconds(track, 3));
    }
```

Create `tests/CinematicCam.Tests/Editing/EditLimitsTests.cs`:

```csharp
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class EditLimitsTests
{
    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(-3f, 0.1f)]
    [InlineData(5f, 5f)]
    [InlineData(9999f, 600f)]
    [InlineData(float.NaN, 0.1f)]
    public void LegClampsToATenthOfASecondAndTenMinutes(float input, float expected)
        => Assert.Equal(expected, EditLimits.Leg(input));

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(9999f, 600f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void HoldClampsToZeroAndTenMinutes(float input, float expected)
        => Assert.Equal(expected, EditLimits.Hold(input));

    [Fact]
    public void PitchClampsToTheGizmoLimit()
    {
        Assert.Equal(TrackAim.PitchLimit, EditLimits.Pitch(2f));
        Assert.Equal(-TrackAim.PitchLimit, EditLimits.Pitch(-2f));
        Assert.Equal(0.3f, EditLimits.Pitch(0.3f));
        Assert.Equal(0f, EditLimits.Pitch(float.NaN));
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(MathF.PI + 0.5f, -MathF.PI + 0.5f)]
    [InlineData(-MathF.PI - 0.5f, MathF.PI - 0.5f)]
    [InlineData(float.NaN, 0f)]
    public void AnglesWrapToAHalfTurnEitherWay(float input, float expected)
        => Assert.Equal(expected, EditLimits.Angle(input), 4);

    [Fact]
    public void FovClampsToTheGamesRange()
    {
        Assert.Equal(0.5f, EditLimits.Fov(0.2f, 0.5f, 1.2f));
        Assert.Equal(1.2f, EditLimits.Fov(2f, 0.5f, 1.2f));
        Assert.Equal(0.8f, EditLimits.Fov(0.8f, 0.5f, 1.2f));
        Assert.Equal(0.5f, EditLimits.Fov(float.NaN, 0.5f, 1.2f));
    }

    [Fact]
    public void CoordinatesKeepTheCurrentValueWhenNotFinite()
    {
        Assert.Equal(-137.1f, EditLimits.Coordinate(-137.1f, 3f));
        Assert.Equal(3f, EditLimits.Coordinate(float.PositiveInfinity, 3f));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "EditLimitsTests|PointSecondsIsWhenThePointIsReached"`
Expected: build errors, `EditLimits` and `PointSeconds` do not exist.

- [ ] **Step 3: Implement**

In `TrackEditing`, after `SetHold`:

```csharp
    /// <summary>The time point <paramref name="index"/> is reached: its first key.</summary>
    public static float PointSeconds(Track track, int index)
    {
        ValidatePointIndex(track, index, "time");
        return track.Timing[FirstKeyIndex(track.Timing, index)].Time;
    }
```

Create `src/CinematicCam.Core/Editing/EditLimits.cs`:

```csharp
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>The ranges the editor's number fields clamp to, so invalid values never reach a track.</summary>
public static class EditLimits
{
    public const float MinLegSeconds = 0.1f;
    public const float MaxSeconds = 600f;

    /// <summary>A leg in seconds, 0.1 to 600; not a number becomes the minimum.</summary>
    public static float Leg(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, MinLegSeconds, MaxSeconds) : MinLegSeconds;

    /// <summary>A hold in seconds, 0 to 600; not a number becomes 0.</summary>
    public static float Hold(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, MaxSeconds) : 0f;

    /// <summary>A pitch in radians within the gizmo's limit; not a number becomes 0.</summary>
    public static float Pitch(float radians) => float.IsFinite(radians) ? Math.Clamp(radians, -TrackAim.PitchLimit, TrackAim.PitchLimit) : 0f;

    /// <summary>A yaw or roll in radians wrapped to within half a turn; not a number becomes 0.</summary>
    public static float Angle(float radians) => float.IsFinite(radians) ? MathF.IEEERemainder(radians, MathF.Tau) : 0f;

    /// <summary>A field of view in radians within the game's range; not a number becomes the minimum.</summary>
    public static float Fov(float radians, float min, float max) => float.IsFinite(radians) ? Math.Clamp(radians, min, max) : min;

    /// <summary>A position coordinate, or <paramref name="current"/> when the value is not finite.</summary>
    public static float Coordinate(float value, float current) => float.IsFinite(value) ? value : current;
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TrackEditing.cs src/CinematicCam.Core/Editing/EditLimits.cs tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs tests/CinematicCam.Tests/Editing/EditLimitsTests.cs
git commit -m "feat(editing) add point times and field limits"
```

---

### Task 2: Scrub state in `SessionState`

*editor § Scrub and jumps.*
- **Editing:** the scrub head shows the last scrubbed time.
- **Live:** dragging holds playback at the dragged moment, then it continues in its prior state, playing or paused. Seeking a finished `Once` shot back un-finishes it, and the scrub head shows playback time.

**Files:**
- Modify: `src/CinematicCam.Core/Session/SessionState.cs`
- Test: `tests/CinematicCam.Tests/Session/SessionEditingTests.cs`

**Interfaces:**
- Consumes: `Director.Seek`, `Pause`, `Resume`, `IsPaused` and `Elapsed`; `SessionState.Duration`.
- Produces, on `SessionState`:
  - `bool Scrubbing { get; }`
  - `double ScrubHead { get; }`
  - `void BeginScrub()`, `void ScrubTo(double time)`, `void EndScrub()`
  - `Edit()`, `Restart()` and `Release()` end any scrub (`Scrubbing` becomes false).

- [ ] **Step 1: Write the failing tests**

Add to `SessionEditingTests` (its `Editing()` helper gives three points with keys at 0, 5 and 10 s, so the duration is 10):

```csharp
    [Fact]
    public void TheScrubHeadWhileEditingIsTheLastScrubbedTimeWithinTheTrack()
    {
        var state = Editing();
        Assert.Equal(0.0, state.ScrubHead);
        state.ScrubTo(4.0);
        Assert.Equal(4.0, state.ScrubHead);
        state.ScrubTo(99.0);
        Assert.Equal(10.0, state.ScrubHead);
        state.ScrubTo(-1.0);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void ScrubbingWhileEditingSetsScrubbingUntilItEnds()
    {
        var state = Editing();
        state.BeginScrub();
        Assert.True(state.Scrubbing);
        state.EndScrub();
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingLiveHoldsPlaybackThenResumesIt()
    {
        var state = Editing();
        state.Play();
        state.BeginScrub();
        Assert.True(state.Director.IsPaused);
        state.ScrubTo(6.0);
        Assert.Equal(6.0, state.ScrubHead, 5);
        state.EndScrub();
        Assert.False(state.Director.IsPaused);
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingAPausedShotLeavesItPaused()
    {
        var state = Editing();
        state.Play();
        state.Stop();
        state.BeginScrub();
        state.ScrubTo(2.0);
        state.EndScrub();
        Assert.True(state.Director.IsPaused);
        Assert.Equal(2.0, state.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingAFinishedOnceShotBackUnfinishesIt()
    {
        var state = Editing();
        state.Play();
        state.Director.Tick(20f);
        Assert.True(state.Director.IsFinished);

        state.BeginScrub();
        state.ScrubTo(3.0);
        state.EndScrub();
        Assert.False(state.Director.IsFinished);
        Assert.False(state.Director.IsPaused);
    }

    [Fact]
    public void ModeChangesEndAScrub()
    {
        var state = Editing();
        state.BeginScrub();
        state.Play();
        Assert.False(state.Scrubbing);

        state.BeginScrub();
        state.Edit();
        Assert.False(state.Scrubbing);

        state.BeginScrub();
        state.Release();
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingDoesNothingWhenOff()
    {
        var state = new SessionState();
        state.BeginScrub();
        state.ScrubTo(3.0);
        Assert.False(state.Scrubbing);
        Assert.Equal(0.0, state.ScrubHead);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter SessionEditingTests`
Expected: build errors, `ScrubHead`, `BeginScrub`, `ScrubTo`, `EndScrub` and `Scrubbing` do not exist.

- [ ] **Step 3: Implement**

In `SessionState`, add two private fields beside the existing ones at the top of the class:

```csharp
    private double scrubTime;
    private bool resumeAfterScrub;
```

Add the public members after `FrameAt`:

```csharp
    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Seconds under the scrub head: playback time while live, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => Mode == CameraMode.Live ? Director.Elapsed : scrubTime;

    /// <summary>Starts dragging the scrub head; live, playback holds until <see cref="EndScrub"/>. No effect when off.</summary>
    public void BeginScrub()
    {
        if (Mode == CameraMode.Off || Scrubbing) return;
        Scrubbing = true;
        resumeAfterScrub = Mode == CameraMode.Live && !Director.IsPaused;
        if (Mode == CameraMode.Live) Director.Pause();
    }

    /// <summary>Moves the scrub head to <paramref name="time"/> within the track; live, playback seeks there. No effect when off.</summary>
    public void ScrubTo(double time)
    {
        if (Mode == CameraMode.Off) return;
        scrubTime = Math.Clamp(time, 0.0, Duration);
        if (Mode == CameraMode.Live) Director.Seek(scrubTime);
    }

    /// <summary>Stops dragging the scrub head; live, playback carries on as it was before.</summary>
    public void EndScrub()
    {
        if (!Scrubbing) return;
        Scrubbing = false;
        if (Mode == CameraMode.Live && resumeAfterScrub) Director.Resume();
    }
```

End a scrub on mode changes by adding `Scrubbing = false;`:
- in `Edit()`, as the first statement of both the `CameraMode.Live` case and the `default` case;
- in `Restart()`, just after the `Refused` return;
- in `Release()`, just after the `return false` line.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Session/SessionState.cs tests/CinematicCam.Tests/Session/SessionEditingTests.cs
git commit -m "feat(session) scrub the shot and hold live playback while scrubbing"
```

---

### Task 3: Scrub, jumps and aim in the session

*editor § Scrub and jumps.*
- **While scrubbing in editing,** the camera shows the frame at the scrub head.
- **On release,** the free-cam flies on from there, with position, roll, FoV and aim.
- **A double-click jump** behaves on the same terms.
- **Edit from Live** keeps the frame's aim by writing `DirH`/`DirV`.

**Files:**
- Modify: `src/CinematicCam.Plugin/Game/FreeCam.cs`
- Modify: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Modify: `src/CinematicCam.Plugin/Session/CameraSession.cs`

**Interfaces:**
- Consumes: `SessionState.Scrubbing`, `ScrubHead`, `BeginScrub`, `ScrubTo` and `EndScrub` (Task 2); `TrackEditing.PointSeconds` (Task 1); `TrackAim.FromDirection` (Core).
- Produces:
  - `FreeCam.Enable(Vector3 startPosition, float startRoll, float startFov)`: the free-cam now holds its FoV.
  - `CameraAccess.ReadFovLimits()` returns `(float Min, float Max)?`.
  - On `CameraSession`: `bool Scrubbing`, `double ScrubHead`, `void BeginScrub()`, `void ScrubTo(double time)`, `void EndScrub()` and `void JumpToPoint(int index)`.
  - `CameraSession.LastFrame` is removed in Task 7, not here, because the gizmo probe still reads it.

- [ ] **Step 1: The free-cam holds its FoV**

In `FreeCam`, add a field `private float fov;` and change `Enable` to:

```csharp
    public void Enable(Vector3 startPosition, float startRoll, float startFov)
    {
        position = startPosition;
        Roll = startRoll;
        fov = startFov;
        lastAngles = null;
        Enabled = true;
    }
```

In `Tick`, replace `CameraAccess.ReadState()?.Fov ?? 0.78f,` with `fov,`.

- [ ] **Step 2: Read the game's FoV range**

Add to `CameraAccess`, after `ReadPitchLimits`:

```csharp
    /// <summary>The narrowest and widest field of view the game allows, in radians.</summary>
    public static (float Min, float Max)? ReadFovLimits()
    {
        if (!TryGetWorldCamera(out var camera)) return null;
        return (camera->MinFoV, camera->MaxFoV);
    }
```

- [ ] **Step 3: Scrub, jumps and FlyFrom in `CameraSession`**

In `Edit()`, change the two cases to:

```csharp
            case EditOutcome.FromOff:
                freeCam.Enable(start.Value.Position, 0f, start.Value.Fov);
                movement.Hold();
                TakeCamera();
                break;
            case EditOutcome.FromLive:
                FlyFrom(start.Value);
                break;
```

In `Frame`, change the editing arm so a scrub shows the scrubbed frame:

```csharp
            CameraMode.Editing => state.Scrubbing && state.FrameAt(state.ScrubHead) is { } scrubbed ? scrubbed : freeCam.Tick(dt),
```

Add after `FrameAt`:

```csharp
    /// <summary>True while the scrub head is being dragged.</summary>
    public bool Scrubbing => state.Scrubbing;

    /// <summary>Seconds under the scrub head.</summary>
    public double ScrubHead => state.ScrubHead;

    /// <summary>Starts dragging the scrub head; while editing the camera shows the scrubbed frame.</summary>
    public void BeginScrub() => state.BeginScrub();

    /// <summary>Moves the scrub head; live, playback seeks there.</summary>
    public void ScrubTo(double time) => state.ScrubTo(time);

    /// <summary>Stops dragging the scrub head; while editing the free-cam flies on from the frame shown.</summary>
    public void EndScrub()
    {
        var fromEditing = state.Mode == CameraMode.Editing && state.Scrubbing;
        state.EndScrub();
        if (fromEditing && state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }

    /// <summary>Puts the free-cam at point <paramref name="index"/> while editing, as a scrub release would.</summary>
    public void JumpToPoint(int index)
    {
        if (state.Mode != CameraMode.Editing || index < 0 || index >= state.Track.Points.Count) return;
        state.ScrubTo(TrackEditing.PointSeconds(state.Track, index));
        if (state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }
```

Add as a private method near `TakeCamera`:

```csharp
    /// <summary>Puts the free-cam at <paramref name="frame"/>, keeping its aim by writing the game's yaw and pitch within its limits.</summary>
    private void FlyFrom(CameraState frame)
    {
        freeCam.Enable(frame.Position, frame.Roll, frame.Fov);
        var (yaw, pitch) = TrackAim.FromDirection(frame.LookAt - frame.Position);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        CameraAccess.WriteAngles(yaw, Math.Clamp(pitch, min, max));
    }
```

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin/Game/FreeCam.cs src/CinematicCam.Plugin/Game/CameraAccess.cs src/CinematicCam.Plugin/Session/CameraSession.cs
git commit -m "feat(camera) show scrubbed frames and fly on from them with their aim"
```

**In-game checks, for the final checklist.** The scrub bar arrives in Task 5, so only Edit from Live can be checked here:
- Play a track, press **Stop** while the camera looks noticeably up or down and to one side, then press **Edit**. **Pass:** the view doesn't snap: the camera keeps the paused frame's aim, and mouse-look carries on from it.

---

### Task 4: The track editor window

*editor § Windows.*
- The mode, fly speed, aim, playback and New track controls carry over from the test window. Undo and Redo buttons are added.
- **Add split button:** the menu's second and third items are disabled with nothing selected.
- **Point list:** rows numbered from 1, click to select, leg and hold fields.
- Everything that changes the track is disabled while live.

**Files:**
- Create: `src/CinematicCam.Plugin/Ui/PendingField.cs`
- Create: `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs`
- Delete: `src/CinematicCam.Plugin/Ui/TestWindow.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes:
  - `EditLimits.Leg` and `EditLimits.Hold` (Task 1);
  - `CameraSession.Duration`, `Selected`, `Select`, `CanUndo`, `CanRedo`, `Undo`, `Redo`, `AddToEnd`, `AddAfterSelected`, `OverwriteSelected`, `ChangeTrack`, `Edit`, `Play`, `Restart`, `Stop`, `Release` and `Speed`;
  - `CameraSession.Scrubbing` and `EndScrub` (Task 3).
- Produces:
  - `internal sealed class PendingField(Func<bool> canApply)`, with `void Draw(string id, float current, string format, float width, Action<float> apply)`, `void Commit()` and `void Clear()`.
  - `internal sealed class TrackEditorWindow(CameraSession session, PendingField fields) : Window`, with the private methods `DrawPoints(bool editing)` and `DrawPointRow(Track track, int index, bool editing)`, and the `DrawStatus()` layout that Task 5 extends.
  - `Plugin` owns one `PendingField`, created with `() => Session.Mode == CameraMode.Editing`. `/ccam` and the plugin's main-UI button open the track editor.

- [ ] **Step 1: Add `PendingField`**

Create `src/CinematicCam.Plugin/Ui/PendingField.cs`:

```csharp
using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Ui;

/// <summary>A number field's typed value, held until the field loses focus and then applied.</summary>
internal sealed class PendingField(Func<bool> canApply)
{
    private (string Id, float Value, Action<float> Apply)? pending;

    /// <summary>Draws a number field showing <paramref name="current"/>, and applies a typed value once the field is no longer active.</summary>
    public void Draw(string id, float current, string format, float width, Action<float> apply)
    {
        var value = pending is { } p && p.Id == id ? p.Value : current;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputFloat($"##{id}", ref value, 0f, 0f, format)) pending = (id, value, apply);
        if (pending is { } done && done.Id == id && !ImGui.IsItemActive()) Commit();
    }

    /// <summary>Applies a typed value still waiting for its field to lose focus; dropped if it can no longer apply.</summary>
    public void Commit()
    {
        if (pending is not { } p) return;
        pending = null;
        if (canApply()) p.Apply(p.Value);
    }

    /// <summary>Drops a typed value without applying it.</summary>
    public void Clear() => pending = null;
}
```

- [ ] **Step 2: Add `TrackEditorWindow`**

Create `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The main editor window: modes, track settings, the point list and status.</summary>
internal sealed class TrackEditorWindow : Window
{
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel"];
    private static readonly string[] PlaybackNames = ["Once", "Loop"];

    private readonly CameraSession session;
    private readonly PendingField fields;
    private CameraMode lastMode;

    public TrackEditorWindow(CameraSession session, PendingField fields)
        : base("Cinematic Cam###ccam-track-editor")
    {
        this.session = session;
        this.fields = fields;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420f, 260f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    /// <summary>Applies an unfinished field edit and ends a scrub, since a closed window never reports either finishing.</summary>
    public override void OnClose()
    {
        fields.Commit();
        if (session.Scrubbing) session.EndScrub();
    }

    public override void Draw()
    {
        if (session.Mode != lastMode)
        {
            fields.Clear();
            lastMode = session.Mode;
        }

        var editing = session.Mode == CameraMode.Editing;
        DrawModeRow();
        DrawSpeedRow();

        ImGui.BeginDisabled(!editing);
        DrawTrackRow();
        ImGui.Separator();
        DrawAddButton();
        ImGui.EndDisabled();

        DrawPoints(editing);
        ImGui.Separator();
        DrawStatus();
    }

    private void DrawModeRow()
    {
        ImGui.TextUnformatted($"Mode: {session.Mode}");

        ImGui.SameLine();
        if (ImGui.Button("Edit")) { fields.Commit(); session.Edit(); }

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Track.Points.Count == 0);
        if (ImGui.Button("Play")) { fields.Commit(); session.Play(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live);
        if (ImGui.Button("Restart")) { fields.Commit(); session.Restart(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode != CameraMode.Live || session.Director.IsPaused);
        if (ImGui.Button("Stop")) { fields.Commit(); session.Stop(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(session.Mode == CameraMode.Off);
        if (ImGui.Button("Release")) { fields.Commit(); session.Release("window"); }
        ImGui.EndDisabled();
    }

    private void DrawSpeedRow()
    {
        var speed = session.Speed;
        var step = speed.Index;
        ImGui.TextUnformatted("Fly speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanUndo);
        if (ImGui.Button("Undo")) { fields.Commit(); session.Undo(); }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanRedo);
        if (ImGui.Button("Redo")) { fields.Commit(); session.Redo(); }
        ImGui.EndDisabled();
    }

    private void DrawTrackRow()
    {
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
        ImGui.TextUnformatted("Aim");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.Combo("##aim", ref aim, AimNames))
        {
            var mode = aim == 0 ? AimMode.AimKeys : AimMode.PathTangent;
            Report(session.ChangeTrack(t => t with { Aim = mode }));
        }

        var playback = session.Track.Playback == PlaybackMode.Once ? 0 : 1;
        ImGui.SameLine();
        ImGui.TextUnformatted("Playback");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        if (ImGui.Combo("##playback", ref playback, PlaybackNames))
        {
            var mode = playback == 0 ? PlaybackMode.Once : PlaybackMode.Loop;
            Report(session.ChangeTrack(t => TrackEditing.SetPlayback(t, mode)));
        }

        ImGui.SameLine();
        if (ImGui.Button("New track")) { fields.Clear(); Report(session.ChangeTrack(_ => TrackEditing.Empty())); }
    }

    private void DrawAddButton()
    {
        if (ImGui.Button("+ Add")) Report(session.AddToEnd());
        ImGui.SameLine(0f, 0f);
        if (ImGui.ArrowButton("##add-menu", ImGuiDir.Down)) ImGui.OpenPopup("add-menu");
        if (!ImGui.BeginPopup("add-menu")) return;

        var selected = session.Selected is not null;
        var ticked = false;
        if (ImGui.MenuItem("Add to end", "Backtick", ref ticked)) Report(session.AddToEnd());
        if (ImGui.MenuItem("Add after selected", "Alt + Backtick", ref ticked, selected)) Report(session.AddAfterSelected());
        if (ImGui.MenuItem("Overwrite selected", "Ctrl + Backtick", ref ticked, selected)) Report(session.OverwriteSelected());
        ImGui.EndPopup();
    }

    private void DrawPoints(bool editing)
    {
        var track = session.Track;
        var footer = (ImGui.GetFrameHeightWithSpacing() * 2f) + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("points", new Vector2(0f, -footer)) && track.Points.Count > 0
            && ImGui.BeginTable("point-table", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("##handle");
            ImGui.TableSetupColumn("#");
            ImGui.TableSetupColumn("Leg (s)");
            ImGui.TableSetupColumn("Hold (s)");
            ImGui.TableSetupColumn("##selected");
            ImGui.TableHeadersRow();

            for (var i = 0; i < track.Points.Count; i++) DrawPointRow(track, i, editing);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private void DrawPointRow(Track track, int index, bool editing)
    {
        var selected = session.Selected == index;
        ImGui.TableNextRow();
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.GripLines.ToIconString());

        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{index + 1}##row{index}", selected)) session.Select(index);

        ImGui.TableNextColumn();
        if (index == 0) ImGui.TextUnformatted("-");
        else fields.Draw($"leg{index}", TrackEditing.LegSeconds(track, index), "%.1f", 70f,
            v => Report(session.ChangeTrack(t => TrackEditing.SetLeg(t, index, EditLimits.Leg(v)))));

        ImGui.TableNextColumn();
        fields.Draw($"hold{index}", TrackEditing.HoldSeconds(track, index), "%.1f", 70f,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, EditLimits.Hold(v)))));

        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        if (selected)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.CaretLeft.ToIconString());
        }
    }

    private void DrawStatus()
    {
        var count = session.Track.Points.Count;
        ImGui.TextUnformatted($"{count} point{(count == 1 ? "" : "s")} | total {session.Duration:0.0} s | {ModeText()}");
    }

    private string ModeText() => session.Mode switch
    {
        CameraMode.Live => session.Director.IsFinished ? "finished" : session.Director.IsPaused ? "paused" : "playing",
        CameraMode.Editing => "editing",
        _ => "off",
    };

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
```

- [ ] **Step 3: Delete the test window and wire the track editor**

Run `git rm src/CinematicCam.Plugin/Ui/TestWindow.cs`. Then in `Plugin`:
- replace the field `private readonly TestWindow testWindow;` with `private readonly TrackEditorWindow trackEditor;`, and add `private readonly PendingField fields;`;
- in the constructor, replace the two test-window lines with:

```csharp
        fields = new PendingField(() => Session.Mode == CameraMode.Editing);
        trackEditor = new TrackEditorWindow(Session, fields);
        windows.AddWindow(trackEditor);
```

- rename `OpenTestWindow` to `OpenTrackEditor`, with the body `trackEditor.IsOpen = true;`. Update its three uses: the `""` command verb, `UiBuilder.OpenMainUi +=`, and `UiBuilder.OpenMainUi -=` in `Dispose`;
- change the help message to `"/ccam opens the editor | release | probe gizmo|aim|input"`. Task 7 drops the probe part.

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin/Ui/PendingField.cs src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs src/CinematicCam.Plugin/Ui/TestWindow.cs src/CinematicCam.Plugin/Plugin.cs
git commit -m "feat(ui) replace the test window with the track editor"
```

**In-game checks, for the final checklist:**
- `/ccam` opens a window titled **Cinematic Cam**, and it can be resized.
- **Undo** and **Redo** are greyed out until there's something to undo or redo, and they work.
- Open the **▾** menu next to **+ Add**. **Pass:** it shows the three items with shortcuts written "Backtick", "Alt + Backtick" and "Ctrl + Backtick". The last two are greyed out with nothing selected.
- Click row 2. **Pass:** marker 2 highlights, and a caret shows beside row 2.
- Type a leg of **0**, then of **9999**. **Pass:** they become 0.1 and 600. Type a hold of **−5**. **Pass:** it becomes 0.
- Press **Play**. **Pass:** the fields and the Add buttons are greyed out while live, and the status reads "playing".

---

### Task 5: Reorder, jump and scrub

*editor § Windows:*
- **Point list:** drag the handle to reorder, and double-click a row to jump.
- **Scrub bar:** from 0 to the shot's length, and it stays usable while live.

*editor § Scrub and jumps.*

**Files:**
- Modify: `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs`

**Interfaces:**
- Consumes:
  - `CameraSession.MovePoint` and `JumpToPoint` (Task 3);
  - `CameraSession.BeginScrub`, `ScrubTo`, `EndScrub`, `ScrubHead` and `Duration`;
  - `PendingField.Commit` (Task 4).
- Produces: nothing new for later tasks.

- [ ] **Step 1: Make the handle a drag source and the row a drop target**

In `TrackEditorWindow`, add:

```csharp
    private const string PointPayload = "CCAM_POINT";
```

In `DrawPointRow`, replace the handle cell's contents with:

```csharp
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.Selectable($"{FontAwesomeIcon.GripLines.ToIconString()}##grip{index}");
        if (editing && ImGui.BeginDragDropSource())
        {
            SetPayload(index);
            ImGui.TextUnformatted($"Point {index + 1}");
            ImGui.EndDragDropSource();
        }

        DropTarget(index, editing);
```

After the row's `Selectable` in the `#` cell, add the double-click jump and a drop target:

```csharp
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) session.JumpToPoint(index);
        DropTarget(index, editing);
```

Add the helpers, and mark the class `unsafe`, as in `internal sealed unsafe class TrackEditorWindow : Window`:

```csharp
    /// <summary>Moves the dragged point to <paramref name="index"/> when it is dropped on this item.</summary>
    private void DropTarget(int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload(PointPayload);
        if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index) Report(session.MovePoint(from, index));
        ImGui.EndDragDropTarget();
    }

    private static void SetPayload(int index) => ImGui.SetDragDropPayload(PointPayload, &index, sizeof(int));
```

- [ ] **Step 2: Add the scrub bar**

In `Draw`, call `DrawScrubBar();` between the second `ImGui.Separator();` and `DrawStatus();`. Add:

```csharp
    private void DrawScrubBar()
    {
        var duration = (float)session.Duration;
        var head = (float)session.ScrubHead;
        var tail = $"{duration:0.0} s   {head:0.0} s";

        ImGui.BeginDisabled(session.Mode == CameraMode.Off || duration <= 0f);
        ImGui.TextUnformatted("0.0");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(tail).X - ImGui.GetStyle().ItemSpacing.X);
        var moved = ImGui.SliderFloat("##scrub", ref head, 0f, MathF.Max(duration, 0.001f), "");
        if (ImGui.IsItemActivated()) { fields.Commit(); session.BeginScrub(); }
        if (moved || ImGui.IsItemActivated()) session.ScrubTo(head);
        if (ImGui.IsItemDeactivated()) session.EndScrub();
        ImGui.SameLine();
        ImGui.TextUnformatted(tail);
        ImGui.EndDisabled();
    }
```

In `DrawPoints`, the footer now holds three rows, so change the footer height to `(ImGui.GetFrameHeightWithSpacing() * 3f) + ImGui.GetStyle().ItemSpacing.Y`.

- [ ] **Step 3: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs
git commit -m "feat(ui) reorder points, jump to them and scrub the shot"
```

**In-game checks, for the final checklist:**
- Drag row 3's grip onto row 1. **Pass:** the list and the markers renumber, point 3's marker is now 1, and its hold went with it. **Ctrl + Z** puts it back.
- Double-click row 2. **Pass:** the camera jumps to point 2 facing where point 2 aims, and you can fly on from there with mouse-look continuing from that aim.
- While editing, drag the scrub bar slowly. **Pass:** the camera follows the shot, and the time on the right changes. Let go. **Pass:** you fly on from that frame, facing its way.
- Press **Play**, then drag the scrub bar. **Pass:** the shot holds where you drag, and carries on playing when you let go. Press **Stop**, scrub, let go. **Pass:** it stays paused at the new time.
- Let a **Once** shot finish, then scrub back. **Pass:** it plays on from there.
- Start dragging the scrub bar while editing, and close the window with its X without letting go of the mouse. **Pass:** the camera isn't stuck on the scrubbed frame; you can fly.

---

### Task 6: The Point window

*editor § Windows.*
- **When it shows:** only while a point is selected in editing mode. It remembers its place and has no close button.
- **Fields:** position in yalms, and yaw, pitch, roll and FoV in degrees. Each applies when editing finishes. Yaw and Pitch are disabled in Direction-of-travel mode.
- **Buttons:** Move/Rotate toggle and Delete.

*editor § Windows: the field ranges.*

**Files:**
- Create: `src/CinematicCam.Plugin/Ui/PointWindow.cs`
- Modify: `src/CinematicCam.Plugin/Editor/PointGizmo.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `PendingField` (Task 4), `EditLimits` (Task 1), `CameraAccess.ReadFovLimits` (Task 3), `CameraSession.Selected`, `ReplacePoint` and `DeleteSelected`, and `PointGizmo.Mode`.
- Produces:
  - `PointGizmo.SetMode(GizmoMode mode)`, ignored while the mouse is still held from a drag. `Mode` gets a private setter, and `Toggle` goes through `SetMode`.
  - `internal sealed class PointWindow(CameraSession session, PointGizmo gizmo, PendingField fields) : Window`.

- [ ] **Step 1: Guard gizmo mode changes**

In `PointGizmo`, change `public GizmoMode Mode { get; set; } = GizmoMode.Move;` to `public GizmoMode Mode { get; private set; } = GizmoMode.Move;`, and replace `Toggle` with:

```csharp
    /// <summary>Switches between Move and Rotate, except while the mouse is still held from a drag.</summary>
    public void Toggle() => SetMode(Mode == GizmoMode.Move ? GizmoMode.Rotate : GizmoMode.Move);

    /// <summary>Sets Move or Rotate, except while the mouse is still held from a drag.</summary>
    public void SetMode(GizmoMode mode)
    {
        if (!Dragging && !waitForRelease) Mode = mode;
    }
```

- [ ] **Step 2: Add `PointWindow`**

Create `src/CinematicCam.Plugin/Ui/PointWindow.cs`:

```csharp
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Editor;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The selected point's number fields, gizmo mode and Delete; shown only while a point is selected in editing mode.</summary>
internal sealed class PointWindow : Window
{
    private const float FieldWidth = 70f;

    private readonly CameraSession session;
    private readonly PointGizmo gizmo;
    private readonly PendingField fields;
    private int? shown;

    public PointWindow(CameraSession session, PointGizmo gizmo, PendingField fields)
        : base("Point###ccam-point", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.gizmo = gizmo;
        this.fields = fields;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    /// <summary>Opens while a point is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var selected = session.Mode == CameraMode.Editing ? session.Selected : null;
        if (selected != shown) fields.Commit();
        shown = selected;
        IsOpen = selected is not null;
        if (selected is { } index) WindowName = $"Point {index + 1}###ccam-point";
    }

    /// <summary>Applies an unfinished field edit, since a closed window never reports the field losing focus.</summary>
    public override void OnClose() => fields.Commit();

    public override void Draw()
    {
        if (session.Selected is not { } index || index >= session.Track.Points.Count) return;
        var point = session.Track.Points[index];

        ImGui.TextUnformatted("Gizmo");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move", gizmo.Mode == GizmoMode.Move)) gizmo.SetMode(GizmoMode.Move);
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", gizmo.Mode == GizmoMode.Rotate)) gizmo.SetMode(GizmoMode.Rotate);

        Field("X", $"x{index}", point.Position.X, "%.1f", v => Edit(index, p => p with { Position = p.Position with { X = EditLimits.Coordinate(v, p.Position.X) } }));
        ImGui.SameLine();
        Field("Y", $"y{index}", point.Position.Y, "%.1f", v => Edit(index, p => p with { Position = p.Position with { Y = EditLimits.Coordinate(v, p.Position.Y) } }));
        ImGui.SameLine();
        Field("Z", $"z{index}", point.Position.Z, "%.1f", v => Edit(index, p => p with { Position = p.Position with { Z = EditLimits.Coordinate(v, p.Position.Z) } }));

        ImGui.BeginDisabled(session.Track.Aim == AimMode.PathTangent);
        Field("Yaw", $"yaw{index}", Degrees(EditLimits.Angle(point.Yaw)), "%.1f°", v => Edit(index, p => p with { Yaw = EditLimits.Angle(Radians(v)) }));
        ImGui.SameLine();
        Field("Pitch", $"pitch{index}", Degrees(point.Pitch), "%.1f°", v => Edit(index, p => p with { Pitch = EditLimits.Pitch(Radians(v)) }));
        ImGui.EndDisabled();

        Field("Roll", $"roll{index}", Degrees(EditLimits.Angle(point.Roll)), "%.1f°", v => Edit(index, p => p with { Roll = EditLimits.Angle(Radians(v)) }));
        ImGui.SameLine();
        Field("FoV", $"fov{index}", Degrees(point.Fov), "%.1f°", v => Edit(index, p => p with { Fov = ClampFov(Radians(v), p.Fov) }));

        if (ImGui.Button("Delete"))
        {
            fields.Clear();
            Report(session.DeleteSelected());
        }
    }

    private void Field(string label, string id, float value, string format, Action<float> apply)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        fields.Draw(id, value, format, FieldWidth, apply);
    }

    /// <summary>Replaces point <paramref name="index"/> with <paramref name="change"/> applied to it as it is now.</summary>
    private void Edit(int index, Func<ControlPoint, ControlPoint> change)
    {
        if (index >= session.Track.Points.Count) return;
        Report(session.ReplacePoint(index, change(session.Track.Points[index])));
    }

    /// <summary>A field of view within the game's range, or <paramref name="current"/> when the range cannot be read.</summary>
    private static float ClampFov(float radians, float current)
        => CameraAccess.ReadFovLimits() is { } limits ? EditLimits.Fov(radians, limits.Min, limits.Max) : current;

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
```

- [ ] **Step 3: Wire it in**

In `Plugin`: add the field `private readonly PointWindow pointWindow;`. In the constructor, after the track editor is added, add:

```csharp
        pointWindow = new PointWindow(Session, pointGizmo, fields);
        windows.AddWindow(pointWindow);
```

`pointGizmo` is a field initialiser, so it already exists by then.

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin/Ui/PointWindow.cs src/CinematicCam.Plugin/Editor/PointGizmo.cs src/CinematicCam.Plugin/Plugin.cs
git commit -m "feat(ui) add the point window"
```

**In-game checks, for the final checklist:**
- Select marker 2. **Pass:** a window titled **Point 2** appears, with no close button. Deselect. **Pass:** it disappears. Select again. **Pass:** it comes back where you left it.
- Type **X** 5 higher and press Enter. **Pass:** marker 2 moves along X, and **Ctrl + Z** undoes it as one step.
- Type **Pitch** 120. **Pass:** it becomes 89.0°. Type **Yaw** 270. **Pass:** it becomes −90.0°.
- Type **FoV** 200. **Pass:** it clamps to the game's widest FoV, and the shot uses it in **Play**.
- Set Aim to **Direction of travel**. **Pass:** Yaw and Pitch are greyed out, while Roll and FoV still work.
- Click **Rotate**, then **Move**. **Pass:** the gizmo switches each time, just as **R** does.
- Type a **Roll** value and, without pressing Enter, click empty sky to deselect. **Pass:** the roll was applied.
- Press **Delete**. **Pass:** the point is removed, the window closes, and **Ctrl + Z** brings it back.

---

### Task 7: Remove the probes

*The handover's Part 2 clean-up: delete the probes and `/ccam probe` once the editor has rebuilt what they showed.*

**Files:**
- Delete: `src/CinematicCam.Plugin/Probes/AimProbe.cs`, `src/CinematicCam.Plugin/Probes/GizmoProbe.cs`, `src/CinematicCam.Plugin/Probes/InputProbe.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`
- Modify: `src/CinematicCam.Plugin/Session/CameraSession.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `/ccam` keeps two verbs, `""` (opens the editor) and `release`. `CameraSession.LastFrame` is removed; the private `lastFrame` field stays, because `Edit` uses it.

- [ ] **Step 1: Delete the probes**

Run `git rm src/CinematicCam.Plugin/Probes/AimProbe.cs src/CinematicCam.Plugin/Probes/GizmoProbe.cs src/CinematicCam.Plugin/Probes/InputProbe.cs`.

- [ ] **Step 2: Remove them from `Plugin`**

In `Plugin`:
- remove the three probe fields, `using CinematicCam.Plugin.Probes;`, the `case "probe":` branch and the whole `OnProbe` method;
- remove `aimProbe.Update();` and `inputProbe.Update(Session.Mode);` from `OnFrameworkUpdate`, and `gizmoProbe.Draw(Session.LastFrame);` and `inputProbe.Draw(Session.Mode);` from `OnDraw`;
- remove the `IGameGui GameGui` and `IObjectTable Objects` service properties, which only the gizmo probe used;
- set the help message to `"/ccam opens the editor | release"`;
- remove `using System.Linq;` if nothing else in the file uses it.

- [ ] **Step 3: Drop `LastFrame`**

In `CameraSession`, delete the `LastFrame` property and its doc comment.

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed. Run `grep -rn "Probe\|LastFrame\|GameGui\|Plugin.Objects" src --include="*.cs"`, which must print nothing.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin/Probes src/CinematicCam.Plugin/Plugin.cs src/CinematicCam.Plugin/Session/CameraSession.cs
git commit -m "chore(plugin) remove the phase 2c-1 probes"
```

**In-game checks, for the final checklist:**
- `/ccam probe gizmo` prints "unknown verb 'probe'" in the log and does nothing else.
- Everything in Part 2a still works: overlay, selection, gizmo, keys and C.

---

## After Part 2b

The controller writes `CHECKLIST.md` from the tasks' in-game checks, and records the results in the spec once the user has worked through it. That completes 2c-1. **2c-2, the curve editor**, is planned next. The spec's Open issues still hold the declined loop-over-the-top case.
