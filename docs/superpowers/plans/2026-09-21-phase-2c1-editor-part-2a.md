# Phase 2c-1 Editor, Part 2a: Keys, Overlay, Selection, Gizmo

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In editing mode, draw the track over the game, select points by clicking their markers, move and rotate the selected point with a gizmo, and drive add, overwrite, undo and redo from the keyboard.

**Architecture:** Core gets the remaining pure logic, built test-first: shot duration, path sampling, near-plane segment clipping, click-versus-drag selection, how a gizmo drag becomes a control point, and gizmo size correction. The plugin gets a new `Editor` folder: an editor layer that draws the overlay on the background draw list and owns one full-screen, click-through window for marker clicks and the gizmo, plus the editor keys. The test window stays until Part 2b replaces it, so each piece can be checked in game with the tools that already exist.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), `Dalamud.Bindings.ImGui`, `Dalamud.Bindings.ImGuizmo`, FFXIVClientStructs (Dalamud's bundled commit `b53cdf38`).

**Spec:** `docs/superpowers/specs/2026-09-21-editor-design.md` (editor), within `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` (main). Citations name the editor spec's sections, e.g. *editor § Selection*.

**Split:** Part 2 is two plans. **2a** (this plan) covers clean-up, keys, overlay, selection and the gizmo. **2b** is written after 2a is verified in game. It covers the track editor and Point windows, the scrub bar, jumps, the undo buttons, and deleting the probes and the test window.

## Global Constraints

- `CinematicCam.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`.
- `tests/CinematicCam.Tests` references Core only.
- Namespaces match folders. **Never create a `CinematicCam.Plugin.Camera` namespace**, because it shadows FFXIVClientStructs' `Camera`.
- Build the plugin with `./build.sh`, never bare `dotnet build`. Keep the build at 0 warnings. Tests: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`.
- Never write `Camera.Distance` or `InterpDistance`. This plan writes no new game fields. `DirH`/`DirV` are written only where `FreeCam.LookAlongRoll` already writes them.
- Nullable values passed straight into `Log.*(..., params object[])` raise CS8604. Format them first.
- Doc comments are one line, stating what a thing is or does. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no trailing period, **no body, no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Work and commit on `main`. Stage only your own files, never `git add -A`. Do not push.
- BDTHPlugin and Cammy have no licence: read them as reference only, never copy code.
- Do not add features, options or behaviour this plan does not name. If something needs a decision, stop and report it; do not settle it yourself.
- If the compiler rejects an ImGui or ImGuizmo call, find the matching overload under `~/code/Dalamud/imgui/` and report the substitution.
- Leave the probes (`src/CinematicCam.Plugin/Probes/`) and `/ccam probe` alone. Part 2b deletes them.

## Decisions this plan relies on

Made by the user on 2026-09-21 and recorded in the spec:
- The Point window has no close button, and its Yaw and Pitch fields are disabled in Direction-of-travel mode (both are 2b).
- The overlay, gizmo and editor keys belong to editing mode and keep working with the track editor closed.
- While a gizmo drag is in progress, the path and the point's marker follow it. It is still one undo step, committed on release.

Technical rulings made while planning (the cost if wrong is in brackets):
- Keys are read from their physical state (`GetAsyncKeyState`) and count as up while the game window is inactive (`Framework.WindowInactive`), so typing in another app does not fly the camera. [If Wine reports that flag wrongly, keys stop working. The Task 5 check catches it.]
- The gizmo rotates in the point's own frame, built from its stored yaw and pitch. In Direction-of-travel mode the stored aim can differ from the direction of travel, so the roll ring can look tilted relative to the path. The roll value it writes is still correct. [Cosmetic only.]
- Constant gizmo size is tried once, in Task 10. If its in-game check fails, the task is reverted and the gizmo stays as it is, as the spec allows. [One in-game round.]

## File map

| File | Responsibility |
|---|---|
| `src/CinematicCam.Core/Session/SessionState.cs` | + `Duration`; private fields moved to the top |
| `src/CinematicCam.Core/Editing/TrackPath.cs` | new: sample the path at even spacing |
| `src/CinematicCam.Core/Camera/ScreenProjection.cs` | + `ProjectSegment`, clipped at the near plane |
| `src/CinematicCam.Core/Editing/ClickSelection.cs` | new: tell a click from a drag, return select or deselect |
| `src/CinematicCam.Core/Editing/GizmoMode.cs` | new: `Move` / `Rotate` |
| `src/CinematicCam.Core/Editing/GizmoEdit.cs` | new: a dragged matrix becomes a control point |
| `src/CinematicCam.Core/Editing/GizmoScale.cs` | new: the clip-space size that keeps the gizmo a fixed screen size |
| `src/CinematicCam.Plugin/Game/PhysicalKeys.cs` | new: physical key state, hiding keys from the game, typing check |
| `src/CinematicCam.Plugin/Game/FreeCam.cs` | fly-down on C; roll-order and tuple-name nits |
| `src/CinematicCam.Plugin/Session/CameraSession.cs` | pass-throughs; add, add-after and overwrite from the camera |
| `src/CinematicCam.Plugin/Editor/EditorKeys.cs` | new: editor key bindings, hidden from the game |
| `src/CinematicCam.Plugin/Editor/EditorView.cs` | new: this frame's matrices and viewport |
| `src/CinematicCam.Plugin/Editor/EditorColours.cs` | new: every overlay colour |
| `src/CinematicCam.Plugin/Editor/Overlay.cs` | new: draws the path, markers and aim lines |
| `src/CinematicCam.Plugin/Editor/PointGizmo.cs` | new: the gizmo on the selected point |
| `src/CinematicCam.Plugin/Editor/EditorLayer.cs` | new: runs the overlay, the click window, selection and the gizmo each frame |
| `src/CinematicCam.Plugin/Plugin.cs` | wires the editor layer and keys |
| `src/CinematicCam.Plugin/Ui/TestWindow.cs` | `Capture point` becomes `Add to end` via `AddToEnd` |

---

### Task 1: Session duration and the Part 1 review nits

**Files:**
- Modify: `src/CinematicCam.Core/Session/SessionState.cs`
- Test: `tests/CinematicCam.Tests/Session/SessionEditingTests.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Produces: `public double Duration { get; }` on `SessionState`, which is the last timing key's time, or 0 with no keys.

- [ ] **Step 1: Write the failing duration test**

Add to `SessionEditingTests`:

```csharp
    [Fact]
    public void DurationIsTheLastKeysTimeAndZeroWhenEmpty()
    {
        var state = new SessionState();
        Assert.Equal(0.0, state.Duration);

        state = Editing();
        Assert.Equal(10.0, state.Duration, 5);

        state.ChangeTrack(t => TrackEditing.SetHold(t, 2, 2f));
        Assert.Equal(12.0, state.Duration, 5);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter DurationIsTheLastKeysTime`
Expected: build error, `SessionState` has no `Duration`.

- [ ] **Step 3: Add `Duration` and move the private fields to the top**

In `SessionState`, move `private readonly EditHistory history = new();` up so the three private fields sit together at the top of the class:

```csharp
    private readonly EditHistory history = new();
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;
```

Add below `Track`:

```csharp
    /// <summary>The track's length in seconds: its last timing key, or 0 with none.</summary>
    public double Duration => Track.Timing.Count == 0 ? 0.0 : Track.Timing[^1].Time;
```

- [ ] **Step 4: Add the two `RequireKeyPerPoint` tests**

Add to `TrackEditingTests`, after `EditsRefuseTimingKeysBetweenPoints`:

```csharp
    [Fact]
    public void EditsRefuseAPointWithNoTimingKey()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[] { new TimingKey(0f, 0f, TangentMode.Auto, 0f, 0f) };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        const string message = "timing keys between points are not supported yet";
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f))).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Delete(track, 0)).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Move(track, 0, 1)).Message);
    }

    [Fact]
    public void EditsRefuseAPointWithMoreThanTwoTimingKeys()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[]
        {
            new TimingKey(0f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(1f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(2f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(7f, 1f, TangentMode.Auto, 0f, 0f),
        };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        const string message = "timing keys between points are not supported yet";
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f))).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Delete(track, 0)).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Move(track, 0, 1)).Message);
    }
```

These cover existing behaviour, so they should pass immediately. If either fails, stop and report. Do not change `RequireKeyPerPoint`.

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Session/SessionState.cs tests/CinematicCam.Tests/Session/SessionEditingTests.cs tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs
git commit -m "feat(session) add track duration and cover key-per-point refusals"
```

---

### Task 2: Path sampling and near-plane clipping

*editor § Overlay: the path is sampled densely along its length and clipped at the near plane.*

**Files:**
- Create: `src/CinematicCam.Core/Editing/TrackPath.cs`
- Modify: `src/CinematicCam.Core/Camera/ScreenProjection.cs`
- Test: `tests/CinematicCam.Tests/Editing/TrackPathTests.cs`
- Test: `tests/CinematicCam.Tests/Camera/ScreenProjectionTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<Vector3> TrackPath.Sample(IReadOnlyList<Vector3> points, float spacing)`
- Produces: `public static (Vector2 Start, Vector2 End)? ScreenProjection.ProjectSegment(Vector3 start, Vector3 end, Matrix4x4 viewProjection, Vector2 viewport, float nearW)`

- [ ] **Step 1: Write the failing path tests**

Create `tests/CinematicCam.Tests/Editing/TrackPathTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class TrackPathTests
{
    private static readonly Vector3[] Line = [Vector3.Zero, new(10f, 0f, 0f), new(20f, 0f, 0f)];

    [Fact]
    public void FewerThanTwoPointsComeBackAsTheyAre()
    {
        Assert.Empty(TrackPath.Sample([], 0.5f));
        Assert.Equal(new[] { new Vector3(1f, 2f, 3f) }, TrackPath.Sample([new Vector3(1f, 2f, 3f)], 0.5f));
    }

    [Fact]
    public void TheSamplesStartAndEndOnTheTrackAndPassThroughEachPoint()
    {
        var samples = TrackPath.Sample(Line, 0.5f);
        Assert.Equal(Line[0], samples[0]);
        Assert.Equal(Line[2].X, samples[^1].X, 3);
        Assert.Contains(samples, s => Vector3.Distance(s, Line[1]) < 1e-3f);
    }

    [Fact]
    public void NeighbouringSamplesAreNoFurtherApartThanTheSpacing()
    {
        var samples = TrackPath.Sample(Line, 0.5f);
        for (var i = 1; i < samples.Count; i++)
            Assert.True(Vector3.Distance(samples[i - 1], samples[i]) <= 0.5f * 1.05f);
    }

    [Fact]
    public void CoincidentPointsStillGiveOneSamplePerSegment()
    {
        var samples = TrackPath.Sample([Vector3.One, Vector3.One], 0.5f);
        Assert.Equal(2, samples.Count);
    }
}
```

- [ ] **Step 2: Write the failing segment tests**

Add to `ScreenProjectionTests` (it already has `Viewport` and `ViewProjection()`, a camera at z = 10 looking at the origin with near plane 0.1):

```csharp
    [Fact]
    public void ASegmentInFrontProjectsToItsEndPoints()
    {
        var a = new Vector3(-1f, 0f, 0f);
        var b = new Vector3(1f, 1f, 0f);
        var segment = ScreenProjection.ProjectSegment(a, b, ViewProjection(), Viewport, 0.1f);
        Assert.NotNull(segment);
        Assert.Equal(ScreenProjection.Project(a, ViewProjection(), Viewport)!.Value, segment!.Value.Start);
        Assert.Equal(ScreenProjection.Project(b, ViewProjection(), Viewport)!.Value, segment.Value.End);
    }

    [Fact]
    public void ASegmentBehindTheCameraIsSkipped()
        => Assert.Null(ScreenProjection.ProjectSegment(new Vector3(0f, 0f, 20f), new Vector3(1f, 0f, 30f), ViewProjection(), Viewport, 0.1f));

    [Fact]
    public void ASegmentCrossingTheNearPlaneIsCutWhereItCrosses()
    {
        // From 10 in front of the camera to 10 behind it, off to one side.
        var front = new Vector3(1f, 0f, 0f);
        var behind = new Vector3(1f, 0f, 20f);
        var segment = ScreenProjection.ProjectSegment(front, behind, ViewProjection(), Viewport, 0.1f)!.Value;

        var cut = ScreenProjection.Project(new Vector3(1f, 0f, 9.9f), ViewProjection(), Viewport)!.Value;
        Assert.True(Vector2.Distance(cut, segment.End) < 1f);

        var reversed = ScreenProjection.ProjectSegment(behind, front, ViewProjection(), Viewport, 0.1f)!.Value;
        Assert.True(Vector2.Distance(cut, reversed.Start) < 1f);
    }
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "TrackPathTests|ScreenProjectionTests"`
Expected: build errors, `TrackPath` and `ProjectSegment` do not exist.

- [ ] **Step 4: Implement `TrackPath`**

Create `src/CinematicCam.Core/Editing/TrackPath.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>Samples a track's path for drawing.</summary>
public static class TrackPath
{
    /// <summary>Caps the samples in one segment, so a very long leg cannot stall a frame.</summary>
    public const int MaxStepsPerSegment = 256;

    /// <summary>Points along the path through <paramref name="points"/>, about <paramref name="spacing"/> metres apart, starting and ending on the track.</summary>
    public static IReadOnlyList<Vector3> Sample(IReadOnlyList<Vector3> points, float spacing)
    {
        if (points.Count < 2) return points.ToArray();

        var table = new ArcLengthTable(points);
        var samples = new List<Vector3> { points[0] };
        for (var segment = 0; segment < table.SegmentCount; segment++)
        {
            var steps = Math.Clamp((int)MathF.Ceiling(table.SegmentLength(segment) / spacing), 1, MaxStepsPerSegment);
            for (var i = 1; i <= steps; i++)
                samples.Add(CatmullRom.Evaluate(points, segment, table.ParameterAt(segment, i / (float)steps)));
        }

        return samples;
    }
}
```

- [ ] **Step 5: Implement `ProjectSegment`**

Replace the body of `ScreenProjection` with:

```csharp
    /// <summary>Pixel position of <paramref name="world"/> with the origin top-left, or null when it is behind the camera.</summary>
    public static Vector2? Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        return clip.W <= float.Epsilon ? null : ToPixels(clip, viewport);
    }

    /// <summary>Pixel end points of a segment cut to the part at least <paramref name="nearW"/> in front of the camera, or null when none of it is.</summary>
    public static (Vector2 Start, Vector2 End)? ProjectSegment(Vector3 start, Vector3 end, Matrix4x4 viewProjection, Vector2 viewport, float nearW)
    {
        var a = Vector4.Transform(new Vector4(start, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(end, 1f), viewProjection);
        if (a.W < nearW && b.W < nearW) return null;

        if (a.W < nearW) a = Vector4.Lerp(a, b, (nearW - a.W) / (b.W - a.W));
        else if (b.W < nearW) b = Vector4.Lerp(b, a, (nearW - b.W) / (a.W - b.W));

        return (ToPixels(a, viewport), ToPixels(b, viewport));
    }

    private static Vector2 ToPixels(Vector4 clip, Vector2 viewport)
    {
        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        return new Vector2((x + 1f) * viewport.X * 0.5f, (1f - y) * viewport.Y * 0.5f);
    }
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/CinematicCam.Core/Editing/TrackPath.cs src/CinematicCam.Core/Camera/ScreenProjection.cs tests/CinematicCam.Tests/Editing/TrackPathTests.cs tests/CinematicCam.Tests/Camera/ScreenProjectionTests.cs
git commit -m "feat(editing) sample the path and clip segments at the near plane"
```

---

### Task 3: Click selection

*editor § Selection: a click is a press and release that moves under a few pixels, and a drag still turns the camera. A click on a marker selects it, and a click on empty space deselects. Clicks over plugin windows are ignored.*

**Files:**
- Create: `src/CinematicCam.Core/Editing/ClickSelection.cs`
- Test: `tests/CinematicCam.Tests/Editing/ClickSelectionTests.cs`

**Interfaces:**
- Produces:
  - `public enum ClickKind { None, Select, Deselect }`
  - `public readonly record struct ClickOutcome(ClickKind Kind, int Index = -1)`
  - `public sealed class ClickSelection` with `public const float MaxTravel = 4f`, `public bool HoldingMarker { get; }`, `public ClickOutcome Update(bool mouseDown, Vector2 cursor, bool overUi, bool overGizmo, int? marker)` and `public void Reset()`.
- `Update` is called once per frame. The outcome arrives on the frame the button is released. The marker and the UI and gizmo flags are taken from the frame the button went down.

- [ ] **Step 1: Write the failing tests**

Create `tests/CinematicCam.Tests/Editing/ClickSelectionTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class ClickSelectionTests
{
    private static readonly Vector2 At = new(100f, 100f);

    private static ClickOutcome Click(ClickSelection clicks, int? marker, bool overUi = false, bool overGizmo = false, Vector2? releaseAt = null)
    {
        Assert.Equal(ClickKind.None, clicks.Update(true, At, overUi, overGizmo, marker).Kind);
        return clicks.Update(false, releaseAt ?? At, false, false, null);
    }

    [Fact]
    public void AClickOnAMarkerSelectsIt()
        => Assert.Equal(new ClickOutcome(ClickKind.Select, 2), Click(new ClickSelection(), 2));

    [Fact]
    public void AClickOnEmptySpaceDeselects()
        => Assert.Equal(ClickKind.Deselect, Click(new ClickSelection(), null).Kind);

    [Fact]
    public void ADragIsNotAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, false, false, 1);
        clicks.Update(true, At + new Vector2(ClickSelection.MaxTravel + 1f, 0f), false, false, null);
        Assert.Equal(ClickKind.None, clicks.Update(false, At, false, false, null).Kind);
    }

    [Fact]
    public void AWobbleUnderTheLimitIsStillAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, false, false, 1);
        clicks.Update(true, At + new Vector2(ClickSelection.MaxTravel - 1f, 0f), false, false, null);
        Assert.Equal(ClickKind.Select, clicks.Update(false, At, false, false, null).Kind);
    }

    [Fact]
    public void PressesOverAPluginWindowOrTheGizmoAreIgnored()
    {
        Assert.Equal(ClickKind.None, Click(new ClickSelection(), null, overUi: true).Kind);
        Assert.Equal(ClickKind.None, Click(new ClickSelection(), 1, overGizmo: true).Kind);
    }

    [Fact]
    public void HoldingMarkerIsTrueOnlyWhileAPressOnAMarkerIsHeld()
    {
        var clicks = new ClickSelection();
        Assert.False(clicks.HoldingMarker);
        clicks.Update(true, At, false, false, 0);
        Assert.True(clicks.HoldingMarker);
        clicks.Update(false, At, false, false, null);
        Assert.False(clicks.HoldingMarker);
    }

    [Fact]
    public void ResetDropsAPressInProgress()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, false, false, 0);
        clicks.Reset();
        Assert.Equal(ClickKind.None, clicks.Update(false, At, false, false, null).Kind);
    }

    [Fact]
    public void NothingHappensWhileTheButtonStaysUp()
        => Assert.Equal(ClickKind.None, new ClickSelection().Update(false, At, false, false, 3).Kind);
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter ClickSelectionTests`
Expected: build error, `ClickSelection` does not exist.

- [ ] **Step 3: Implement**

Create `src/CinematicCam.Core/Editing/ClickSelection.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core.Editing;

/// <summary>What a finished click does to the selection.</summary>
public enum ClickKind { None, Select, Deselect }

/// <summary>A click's effect; <see cref="Index"/> is the marker for <see cref="ClickKind.Select"/>.</summary>
public readonly record struct ClickOutcome(ClickKind Kind, int Index = -1);

/// <summary>Tells a click from a drag and turns clicks on markers or empty space into selection changes.</summary>
public sealed class ClickSelection
{
    /// <summary>Pixels the cursor may move between press and release and still count as a click.</summary>
    public const float MaxTravel = 4f;

    private bool down;
    private bool ignored;
    private bool travelled;
    private Vector2 start;
    private int? marker;

    /// <summary>True while a press that started on a marker is held.</summary>
    public bool HoldingMarker => down && marker is not null;

    /// <summary>Feeds one frame of mouse state; returns the outcome on the frame a click is released.</summary>
    public ClickOutcome Update(bool mouseDown, Vector2 cursor, bool overUi, bool overGizmo, int? marker)
    {
        if (mouseDown && !down)
        {
            down = true;
            start = cursor;
            travelled = false;
            ignored = overUi || overGizmo;
            this.marker = marker;
            return default;
        }

        if (mouseDown)
        {
            if (Vector2.Distance(cursor, start) > MaxTravel) travelled = true;
            return default;
        }

        if (!down) return default;
        down = false;
        if (ignored || travelled) return default;
        return this.marker is { } m ? new ClickOutcome(ClickKind.Select, m) : new ClickOutcome(ClickKind.Deselect);
    }

    /// <summary>Forgets a press in progress.</summary>
    public void Reset() => down = false;
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Editing/ClickSelection.cs tests/CinematicCam.Tests/Editing/ClickSelectionTests.cs
git commit -m "feat(editing) tell marker clicks from camera drags"
```

---

### Task 4: Gizmo edits and gizmo size

*editor § Gizmo. Move uses world axes and Rotate uses rings in the point's own frame, with only roll in Direction-of-travel mode. The gizmo commits only if the pose changed (Part 1 review). Its screen size is constant if that's cheap.*

**Files:**
- Create: `src/CinematicCam.Core/Editing/GizmoMode.cs`
- Create: `src/CinematicCam.Core/Editing/GizmoEdit.cs`
- Create: `src/CinematicCam.Core/Editing/GizmoScale.cs`
- Test: `tests/CinematicCam.Tests/Editing/GizmoEditTests.cs`
- Test: `tests/CinematicCam.Tests/Editing/GizmoScaleTests.cs`

**Interfaces:**
- Consumes: `PoseMatrix.From(Vector3 position, float yaw, float pitch, float roll)` and `PoseMatrix.ToPose(Matrix4x4)` (Part 1).
- Produces:
  - `public enum GizmoMode { Move, Rotate }`
  - `public static ControlPoint GizmoEdit.Apply(ControlPoint original, Matrix4x4 dragged, GizmoMode mode, AimMode aim)`, which returns **the same `original` instance** when nothing changed beyond `GizmoEdit.Tolerance`.
  - `public static float GizmoScale.ClipSize(Vector3 position, Vector3 cameraRight, Matrix4x4 gizmoViewProjection, Matrix4x4 screenViewProjection, float target)`

- [ ] **Step 1: Write the failing edit tests**

Create `tests/CinematicCam.Tests/Editing/GizmoEditTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class GizmoEditTests
{
    private static readonly ControlPoint Original = new(new Vector3(1f, 2f, 3f), 0.7f, 0.3f, 0.9f, 0.4f);

    private static Matrix4x4 Pose(Vector3 position, float yaw, float pitch, float roll) => PoseMatrix.From(position, yaw, pitch, roll);

    [Theory]
    [InlineData(GizmoMode.Move, AimMode.AimKeys)]
    [InlineData(GizmoMode.Rotate, AimMode.AimKeys)]
    [InlineData(GizmoMode.Rotate, AimMode.PathTangent)]
    public void AnUnmovedGizmoReturnsTheOriginal(GizmoMode mode, AimMode aim)
    {
        var dragged = Pose(Original.Position, Original.Yaw, Original.Pitch, Original.Roll);
        Assert.Same(Original, GizmoEdit.Apply(Original, dragged, mode, aim));
    }

    [Fact]
    public void AYawThatWrappedAFullTurnCountsAsUnchanged()
    {
        var turned = Original with { Yaw = Original.Yaw + MathF.Tau };
        var dragged = Pose(turned.Position, Original.Yaw, Original.Pitch, Original.Roll);
        Assert.Same(turned, GizmoEdit.Apply(turned, dragged, GizmoMode.Rotate, AimMode.AimKeys));
    }

    [Fact]
    public void MoveTakesOnlyThePosition()
    {
        var dragged = Pose(new Vector3(5f, 6f, 7f), Original.Yaw + 0.2f, Original.Pitch, Original.Roll);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Move, AimMode.AimKeys);
        Assert.Equal(Original with { Position = new Vector3(5f, 6f, 7f) }, edited);
    }

    [Fact]
    public void RotateWithRecordedAimTakesYawPitchAndRollButNotPosition()
    {
        var dragged = Pose(new Vector3(9f, 9f, 9f), 1.2f, -0.4f, 0.1f);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Rotate, AimMode.AimKeys);
        Assert.Equal(Original.Position, edited.Position);
        Assert.Equal(1.2f, edited.Yaw, 4);
        Assert.Equal(-0.4f, edited.Pitch, 4);
        Assert.Equal(0.1f, edited.Roll, 4);
        Assert.Equal(Original.Fov, edited.Fov);
    }

    [Fact]
    public void RotateWithDirectionOfTravelTakesOnlyRoll()
    {
        var dragged = Pose(Original.Position, Original.Yaw, Original.Pitch, Original.Roll + 0.5f);
        var edited = GizmoEdit.Apply(Original, dragged, GizmoMode.Rotate, AimMode.PathTangent);
        Assert.Equal(Original.Yaw, edited.Yaw);
        Assert.Equal(Original.Pitch, edited.Pitch);
        Assert.Equal(Original.Roll + 0.5f, edited.Roll, 4);
    }
}
```

- [ ] **Step 2: Write the failing scale tests**

Create `tests/CinematicCam.Tests/Editing/GizmoScaleTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class GizmoScaleTests
{
    private static Matrix4x4 ViewProjection()
        => Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 10f), Vector3.Zero, Vector3.UnitY)
         * Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);

    [Fact]
    public void MatchingMatricesNeedNoCorrection()
        => Assert.Equal(0.1f, GizmoScale.ClipSize(Vector3.Zero, Vector3.UnitX, ViewProjection(), ViewProjection(), 0.1f), 5);

    [Fact]
    public void AGizmoProjectionTwiceTheSizeOnScreenGetsTwiceTheClipSize()
    {
        var doubled = ViewProjection() * Matrix4x4.CreateScale(2f, 2f, 1f);
        Assert.Equal(0.2f, GizmoScale.ClipSize(Vector3.Zero, Vector3.UnitX, doubled, ViewProjection(), 0.1f), 4);
    }

    [Fact]
    public void APointBehindTheCameraFallsBackToTheTarget()
        => Assert.Equal(0.1f, GizmoScale.ClipSize(new Vector3(0f, 0f, 20f), Vector3.UnitX, ViewProjection(), Matrix4x4.Identity with { M44 = 0f }, 0.1f));
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "GizmoEditTests|GizmoScaleTests"`
Expected: build errors, the types do not exist.

- [ ] **Step 4: Implement `GizmoMode` and `GizmoEdit`**

Create `src/CinematicCam.Core/Editing/GizmoMode.cs`:

```csharp
namespace CinematicCam.Core.Editing;

/// <summary>Whether the gizmo moves or rotates the selected point.</summary>
public enum GizmoMode { Move, Rotate }
```

Create `src/CinematicCam.Core/Editing/GizmoEdit.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>Turns a dragged gizmo matrix into the control point it describes.</summary>
public static class GizmoEdit
{
    /// <summary>Changes below this, in metres or radians, are matrix round-off, not a drag.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>The edited point, or <paramref name="original"/> itself when the drag changed nothing.</summary>
    public static ControlPoint Apply(ControlPoint original, Matrix4x4 dragged, GizmoMode mode, AimMode aim)
    {
        if (mode == GizmoMode.Move)
        {
            var position = dragged.Translation;
            return Vector3.Distance(position, original.Position) <= Tolerance ? original : original with { Position = position };
        }

        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(dragged);
        var edited = aim == AimMode.AimKeys
            ? original with { Yaw = yaw, Pitch = pitch, Roll = roll }
            : original with { Roll = roll };

        return Same(edited.Yaw, original.Yaw) && Same(edited.Pitch, original.Pitch) && Same(edited.Roll, original.Roll)
            ? original
            : edited;
    }

    private static bool Same(float a, float b) => MathF.Abs(MathF.IEEERemainder(a - b, MathF.Tau)) <= Tolerance;
}
```

- [ ] **Step 5: Implement `GizmoScale`**

ImGuizmo sizes its gizmo as `gizmoSize / L`, where `L` is the clip-space length of the camera's right vector at the gizmo, measured through the matrices ImGuizmo is given (upstream `ImGuizmo.cpp`, `ComputeContext`). The matrices it gets are fixed up for ImGuizmo, not the ones the screen uses. This corrects for the difference.

Create `src/CinematicCam.Core/Editing/GizmoScale.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core.Editing;

/// <summary>Keeps the gizmo a fixed size on screen when it is drawn with different matrices from the screen's.</summary>
public static class GizmoScale
{
    private const float Epsilon = 1e-6f;

    /// <summary>The clip-space size to give ImGuizmo so the gizmo appears <paramref name="target"/> tall on screen; <paramref name="target"/> itself if either length is degenerate.</summary>
    public static float ClipSize(Vector3 position, Vector3 cameraRight, Matrix4x4 gizmoViewProjection, Matrix4x4 screenViewProjection, float target)
    {
        var drawn = ClipLength(position, cameraRight, gizmoViewProjection);
        var actual = ClipLength(position, cameraRight, screenViewProjection);
        return drawn > Epsilon && actual > Epsilon ? target * drawn / actual : target;
    }

    private static float ClipLength(Vector3 position, Vector3 direction, Matrix4x4 viewProjection)
    {
        var a = Vector4.Transform(new Vector4(position, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(position + direction, 1f), viewProjection);
        if (a.W <= Epsilon || b.W <= Epsilon) return 0f;
        return Vector2.Distance(new Vector2(a.X, a.Y) / a.W, new Vector2(b.X, b.Y) / b.W);
    }
}
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/CinematicCam.Core/Editing/GizmoMode.cs src/CinematicCam.Core/Editing/GizmoEdit.cs src/CinematicCam.Core/Editing/GizmoScale.cs tests/CinematicCam.Tests/Editing/GizmoEditTests.cs tests/CinematicCam.Tests/Editing/GizmoScaleTests.cs
git commit -m "feat(editing) turn gizmo drags into point edits"
```

---

### Task 5: Session pass-throughs and fly-down on C

*editor § Keys and input: C flies down and replaces Ctrl, and is hidden from the game. The handover's clean-up list: `CapturePoint` goes through `AddToEnd`, `CameraSession` gets the pass-throughs, and the two `LookAlongRoll` nits.*

**Files:**
- Create: `src/CinematicCam.Plugin/Game/PhysicalKeys.cs`
- Create: `src/CinematicCam.Plugin/Editor/EditorKeys.cs`
- Modify: `src/CinematicCam.Plugin/Game/FreeCam.cs`
- Modify: `src/CinematicCam.Plugin/Session/CameraSession.cs`
- Modify: `src/CinematicCam.Plugin/Ui/TestWindow.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `SessionState.Duration` (Task 1), and `SessionState`'s `Selected`, `Select`, `AddToEnd`, `AddAfterSelected`, `OverwriteSelected`, `ReplacePoint`, `DeleteSelected`, `MovePoint`, `Undo`, `Redo`, `CanUndo`, `CanRedo` and `FrameAt` (Part 1).
- Produces, on `CameraSession`:
  - `int? Selected`, `void Select(int? index)`, `bool CanUndo`, `bool CanRedo`, `bool Undo()`, `bool Redo()`, `double Duration`, `CameraState? FrameAt(double time)`
  - `string? AddToEnd()`, `string? AddAfterSelected()`, `string? OverwriteSelected()`, all taking the current camera
  - `string? ReplacePoint(int index, ControlPoint point)`, `string? DeleteSelected()`, `string? MovePoint(int from, int to)`
  - `CapturePoint` is removed.
- Produces, on `PhysicalKeys` (internal static, `CinematicCam.Plugin.Game`): `bool IsDown(VirtualKey key)`, `void Hide(VirtualKey key)`, `bool IsTyping()`.
- Produces: `internal sealed class EditorKeys` (`CinematicCam.Plugin.Editor`) with `void Update(CameraSession session)`, called from `Framework.Update`. Task 8 extends it.

- [ ] **Step 1: Add `PhysicalKeys`**

Create `src/CinematicCam.Plugin/Game/PhysicalKeys.cs`:

```csharp
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace CinematicCam.Plugin.Game;

/// <summary>Reads keys from their physical state, since Dalamud releases non-modifier keys in ImGui each frame, and hides them from the game.</summary>
internal static unsafe class PhysicalKeys
{
    /// <summary>True while the key is held and the game window is active.</summary>
    public static bool IsDown(VirtualKey key)
    {
        var framework = GameFramework.Instance();
        if (framework == null || framework->WindowInactive) return false;
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    /// <summary>Clears a key from the game's buffer; it stays cleared while held.</summary>
    public static void Hide(VirtualKey key) => Plugin.KeyState[key] = false;

    /// <summary>True while typing in game chat or a plugin text field.</summary>
    public static bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return (module != null && module->IsTextInputActive()) || ImGui.GetIO().WantTextInput;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
```

- [ ] **Step 2: Fly down on C, and fix the two nits in `FreeCam`**

In `FreeCam.Tick`, update `Roll` before correcting the look, and use `PhysicalKeys.IsTyping()`:

```csharp
    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled) return null;

        var typing = PhysicalKeys.IsTyping();
        if (!typing) Roll = Wrap(Roll + (ReadRoll() * RollRate * deltaSeconds));
        var (yaw, pitch) = LookAlongRoll(CameraAccess.ReadAngles() ?? (0f, 0f));
        var input = typing ? Vector3.Zero : ReadInput();
        var speed = BaseSpeed * Speed.Multiplier * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, input, yaw, pitch, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraAccess.ReadState()?.Fov ?? 0.78f,
            Roll);
    }
```

In `LookAlongRoll`, name the result tuple:

```csharp
        (float Yaw, float Pitch) result = (Wrap(last.Yaw + turnYaw), Math.Clamp(last.Pitch + turnPitch, min, max));

        CameraAccess.WriteAngles(result.Yaw, result.Pitch);
        lastAngles = result;
        return result;
```

In `ReadInput`, replace the Ctrl line and its comment with:

```csharp
        if (PhysicalKeys.IsDown(VirtualKey.C)) up -= 1f;
```

Delete `FreeCam.IsTyping` and the now-unused `using FFXIVClientStructs.FFXIV.Client.UI;`. Update the class summary to say "space and C".

- [ ] **Step 3: Add `EditorKeys`, hiding C for now**

Create `src/CinematicCam.Plugin/Editor/EditorKeys.cs`:

```csharp
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Editor;

/// <summary>The editing-mode key bindings, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    /// <summary>Reads the keys and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session)
    {
        if (session.Mode != CameraMode.Editing || PhysicalKeys.IsTyping()) return;

        if (PhysicalKeys.IsDown(VirtualKey.C)) PhysicalKeys.Hide(VirtualKey.C);
    }
}
```

- [ ] **Step 4: Add the `CameraSession` pass-throughs and the add and overwrite methods**

In `CameraSession`, replace `CapturePoint` with:

```csharp
    /// <summary>The selected point's index, or null.</summary>
    public int? Selected => state.Selected;

    /// <summary>Selects a point while editing; null or out of range clears the selection.</summary>
    public void Select(int? index) => state.Select(index);

    /// <summary>The track's length in seconds.</summary>
    public double Duration => state.Duration;

    /// <summary>The track's frame at <paramref name="time"/> seconds, or null with no points.</summary>
    public CameraState? FrameAt(double time) => state.FrameAt(time);

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => state.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => state.CanRedo;

    /// <summary>Restores the track and selection before the last change.</summary>
    public bool Undo() => state.Undo();

    /// <summary>Re-applies the last undone change.</summary>
    public bool Redo() => state.Redo();

    /// <summary>Adds the current camera to the end of the track. Returns why it was refused, or null.</summary>
    public string? AddToEnd() => WithCurrentPoint(state.AddToEnd);

    /// <summary>Adds the current camera after the selected point and selects it. Returns why it was refused, or null.</summary>
    public string? AddAfterSelected() => WithCurrentPoint(state.AddAfterSelected);

    /// <summary>Replaces the selected point with the current camera. Returns why it was refused, or null.</summary>
    public string? OverwriteSelected() => WithCurrentPoint(state.OverwriteSelected);

    /// <summary>Replaces point <paramref name="index"/>, keeping its timing. Returns why it was refused, or null.</summary>
    public string? ReplacePoint(int index, ControlPoint point) => state.ReplacePoint(index, point);

    /// <summary>Deletes the selected point. Returns why it was refused, or null.</summary>
    public string? DeleteSelected() => state.DeleteSelected();

    /// <summary>Moves a point in the order. Returns why it was refused, or null.</summary>
    public string? MovePoint(int from, int to) => state.MovePoint(from, to);

    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be added while editing.";

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return "Cannot read the camera.";

        var s = camera.Value;
        var (yaw, pitch) = angles.Value;
        return edit(new ControlPoint(s.Position, yaw, pitch, s.Fov, freeCam.Roll));
    }
```

- [ ] **Step 5: Use `AddToEnd` in the test window**

In `TestWindow.DrawTrackRow`, replace the Capture button line with:

```csharp
        if (ImGui.Button("Add to end")) error = session.AddToEnd();
```

- [ ] **Step 6: Wire `EditorKeys` into the plugin**

In `Plugin`: add `using CinematicCam.Plugin.Editor;` and the field `private readonly EditorKeys editorKeys = new();`. In `OnFrameworkUpdate`, call `editorKeys.Update(Session);` right after `inputProbe.Update(Session.Mode);`.

- [ ] **Step 7: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/CinematicCam.Plugin/Game/PhysicalKeys.cs src/CinematicCam.Plugin/Editor/EditorKeys.cs src/CinematicCam.Plugin/Game/FreeCam.cs src/CinematicCam.Plugin/Session/CameraSession.cs src/CinematicCam.Plugin/Ui/TestWindow.cs src/CinematicCam.Plugin/Plugin.cs
git commit -m "feat(camera) fly down on c and route adds through the session"
```

**In-game check (the user's).** `/ccam`, then press **Edit**.
1. Hold **C**. **Pass:** the camera flies down and the Character window does not open.
2. Hold **Ctrl**. **Pass:** the camera does not move.
3. Roll with **E** to about 90°, then drag the mouse right. **Pass:** the view turns right on screen, as it did before this change.
4. Click in another app, such as Finder, and type a `c` there. **Pass:** the camera does not move. Click back into the game.
5. Press **Add to end** twice from two places. **Pass:** the point list shows two points.

---

### Task 6: The overlay

*editor § Overlay. It's drawn in editing mode only, on the background draw list, with our own projection. It has the path, numbered markers with the selected one highlighted, and aim lines in Recorded-aim mode only. Colours live in one place.*

**Files:**
- Create: `src/CinematicCam.Plugin/Editor/EditorView.cs`
- Create: `src/CinematicCam.Plugin/Editor/EditorColours.cs`
- Create: `src/CinematicCam.Plugin/Editor/Overlay.cs`
- Create: `src/CinematicCam.Plugin/Editor/EditorLayer.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `TrackPath.Sample`, `ScreenProjection.Project` and `ProjectSegment` (Task 2); `CameraSession.Selected` (Task 5).
- Produces:
  - `internal readonly record struct EditorView(Matrix4x4 ViewProjection, Matrix4x4 GizmoView, Matrix4x4 GizmoProjection, float Near, Vector2 Origin, Vector2 Size, Vector3 CameraRight)` with `Vector2? ToScreen(Vector3 world)` (absolute pixels) and `static EditorView? Read()`.
  - `internal sealed class Overlay` with `IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected)`, which returns each marker's absolute screen position, or null when it is off screen.
  - `internal sealed class EditorLayer` with `void Draw()`, called from `UiBuilder.Draw`. Tasks 7 and 9 extend it.

- [ ] **Step 1: Add `EditorView`**

The gizmo matrices use BDTHPlugin's fix-up (reference only; re-derived in probe 1, see `GizmoProbe.DrawGizmo`).

Create `src/CinematicCam.Plugin/Editor/EditorView.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Plugin.Game;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Editor;

/// <summary>This frame's world-camera matrices and the main viewport, for the overlay and the gizmo.</summary>
internal readonly record struct EditorView(
    Matrix4x4 ViewProjection, Matrix4x4 GizmoView, Matrix4x4 GizmoProjection,
    float Near, Vector2 Origin, Vector2 Size, Vector3 CameraRight)
{
    /// <summary>Absolute screen position of <paramref name="world"/>, or null when it is behind the camera.</summary>
    public Vector2? ToScreen(Vector3 world)
        => ScreenProjection.Project(world, ViewProjection, Size) is { } p ? Origin + p : null;

    /// <summary>Reads the world camera's matrices, or null when there is no camera yet.</summary>
    public static unsafe EditorView? Read()
    {
        if (!CameraAccess.TryGetWorldCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        var render = scene->RenderCamera;
        if (render == null) return null;

        Matrix4x4 view = scene->ViewMatrix;
        Matrix4x4 projection = render->ProjectionMatrix;
        var near = render->NearPlane;
        var far = render->FarPlane;

        // BDTHPlugin's fix-up: re-express the game's reversed-Z, infinite-far projection for ImGuizmo.
        var gizmoProjection = projection;
        gizmoProjection.M43 = -(far / (far - near) * near);
        gizmoProjection.M33 = -((far + near) / (far - near));
        var gizmoView = view;
        gizmoView.M44 = 1f;

        var right = Matrix4x4.Invert(view, out var inverse)
            ? Vector3.Normalize(new Vector3(inverse.M11, inverse.M12, inverse.M13))
            : Vector3.UnitX;

        var viewport = ImGuiHelpers.MainViewport;
        return new EditorView(view * projection, gizmoView, gizmoProjection, near, viewport.Pos, viewport.Size, right);
    }
}
```

- [ ] **Step 2: Add `EditorColours`**

Create `src/CinematicCam.Plugin/Editor/EditorColours.cs`:

```csharp
namespace CinematicCam.Plugin.Editor;

/// <summary>Every overlay colour, as ImGui ABGR.</summary>
internal static class EditorColours
{
    public const uint Path = 0xC0FFD080;
    public const uint AimLine = 0xC080FFFF;
    public const uint Marker = 0xE0303030;
    public const uint MarkerRing = 0xFFFFFFFF;
    public const uint Selected = 0xFF00A0FF;
    public const uint MarkerText = 0xFFFFFFFF;
}
```

- [ ] **Step 3: Add `Overlay`**

Create `src/CinematicCam.Plugin/Editor/Overlay.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace CinematicCam.Plugin.Editor;

/// <summary>Draws the track's path, numbered markers and aim lines over the game.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 10f;
    private const float PathSpacing = 0.25f;
    private const float AimLineLength = 1.5f;

    private IReadOnlyList<ControlPoint>? sampledPoints;
    private IReadOnlyList<Vector3> samples = [];

    /// <summary>Draws <paramref name="track"/> and returns each marker's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track);
        if (track.Aim == AimMode.AimKeys) DrawAimLines(list, view, track);
        return DrawMarkers(list, view, track, selected);
    }

    private void DrawPath(ImDrawListPtr list, EditorView view, Track track)
    {
        if (!ReferenceEquals(sampledPoints, track.Points))
        {
            samples = TrackPath.Sample(track.Points.Select(p => p.Position).ToArray(), PathSpacing);
            sampledPoints = track.Points;
        }

        for (var i = 1; i < samples.Count; i++)
        {
            if (ScreenProjection.ProjectSegment(samples[i - 1], samples[i], view.ViewProjection, view.Size, view.Near) is { } s)
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, EditorColours.Path, 2f);
        }
    }

    private static void DrawAimLines(ImDrawListPtr list, EditorView view, Track track)
    {
        foreach (var point in track.Points)
        {
            var ahead = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch)) * AimLineLength;
            if (ScreenProjection.ProjectSegment(point.Position, point.Position + ahead, view.ViewProjection, view.Size, view.Near) is { } s)
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, EditorColours.AimLine, 2f);
        }
    }

    private static Vector2?[] DrawMarkers(ImDrawListPtr list, EditorView view, Track track, int? selected)
    {
        var screens = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            if (view.ToScreen(track.Points[i].Position) is not { } at) continue;
            screens[i] = at;

            var ring = i == selected ? EditorColours.Selected : EditorColours.MarkerRing;
            list.AddCircleFilled(at, MarkerRadius, EditorColours.Marker);
            list.AddCircle(at, MarkerRadius, ring, 0, i == selected ? 3f : 1.5f);

            var label = (i + 1).ToString();
            list.AddText(at - (ImGui.CalcTextSize(label) / 2f), EditorColours.MarkerText, label);
        }

        return screens;
    }
}
```

- [ ] **Step 4: Add `EditorLayer`, overlay only for now**

Create `src/CinematicCam.Plugin/Editor/EditorLayer.cs`:

```csharp
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Session;

namespace CinematicCam.Plugin.Editor;

/// <summary>Everything drawn over the game in editing mode: the overlay, marker clicks and the gizmo.</summary>
internal sealed class EditorLayer
{
    private readonly CameraSession session;
    private readonly Overlay overlay = new();

    public EditorLayer(CameraSession session) => this.session = session;

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (session.Mode != CameraMode.Editing) return;
        if (EditorView.Read() is not { } view) return;

        overlay.Draw(view, session.Track, session.Selected);
    }
}
```

- [ ] **Step 5: Wire it in**

In `Plugin`: add the field `private readonly EditorLayer editorLayer;`, construct it in the constructor right after `Session` (`editorLayer = new EditorLayer(Session);`), and call `editorLayer.Draw();` in `OnDraw` just before `gizmoProbe.Draw(Session.LastFrame);`.

- [ ] **Step 6: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/CinematicCam.Plugin/Editor/EditorView.cs src/CinematicCam.Plugin/Editor/EditorColours.cs src/CinematicCam.Plugin/Editor/Overlay.cs src/CinematicCam.Plugin/Editor/EditorLayer.cs src/CinematicCam.Plugin/Plugin.cs
git commit -m "feat(editor) draw the path, markers and aim lines"
```

**In-game check (the user's).** Press **Edit** and add four points in a curve with **Add to end**, turning between them.
1. **Pass:** a line runs smoothly through four circles numbered 1 to 4, and stays on them while you fly and turn.
2. Fly so part of the path runs past and behind you. **Pass:** no line streaks across the screen.
3. **Pass:** each circle has a short line pointing where you were looking when you added it. Set Aim to **Direction of travel**. **Pass:** those lines disappear.
4. Press **Play**. **Pass:** the overlay is gone while live. Press **Release**. **Pass:** it stays gone while off.
5. Drag the test window over a marker. **Pass:** the window draws on top of the marker.

---

### Task 7: Selecting points by clicking markers

*editor § Selection. Left-click a marker to select it; the nearest marker wins. Left-click empty space to deselect. Clicks over plugin windows are ignored, and a click on a marker does not reach the game.*

**Files:**
- Modify: `src/CinematicCam.Plugin/Editor/EditorLayer.cs`

**Interfaces:**
- Consumes: `ClickSelection` (Task 3), `MarkerHitTest.Nearest` (Part 1), and `Overlay.Draw` and `Overlay.MarkerRadius` (Task 6).
- Produces: `EditorLayer` opens one full-screen window, `##ccam-editor`. It is click-through (`NoInputs`) except while the cursor is over a marker, or a press that began on one is held. Task 9 adds the gizmo to this window and to the capture condition.

- [ ] **Step 1: Add the click window and selection**

Replace `EditorLayer` with:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Editor;

/// <summary>Everything drawn over the game in editing mode: the overlay, marker clicks and the gizmo.</summary>
internal sealed class EditorLayer
{
    private const float HitRadius = Overlay.MarkerRadius + 4f;

    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

    private readonly CameraSession session;
    private readonly Overlay overlay = new();
    private readonly ClickSelection clicks = new();

    public EditorLayer(CameraSession session) => this.session = session;

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (session.Mode != CameraMode.Editing) { clicks.Reset(); return; }
        if (EditorView.Read() is not { } view) return;

        var markers = overlay.Draw(view, session.Track, session.Selected);
        var io = ImGui.GetIO();
        var hovered = MarkerHitTest.Nearest(markers, io.MousePos, HitRadius);

        // The window takes the mouse only over a marker, so those clicks never reach the game.
        var flags = BaseFlags;
        if (hovered is null && !clicks.HoldingMarker) flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(view.Origin);
        ImGui.SetNextWindowSize(view.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("##ccam-editor", flags))
        {
            var overUi = io.WantCaptureMouse && !ImGui.IsWindowHovered();
            Apply(clicks.Update(ImGui.IsMouseDown(ImGuiMouseButton.Left), io.MousePos, overUi, false, hovered));
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void Apply(ClickOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case ClickKind.Select:
                session.Select(outcome.Index);
                break;
            case ClickKind.Deselect:
                session.Select(null);
                break;
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 3: Commit**

```bash
git add src/CinematicCam.Plugin/Editor/EditorLayer.cs
git commit -m "feat(editor) select points by clicking their markers"
```

**In-game check (the user's).** Press **Edit** with the four-point track from Task 6. Fly so a targetable NPC or object sits right behind marker 2.
1. Click marker 2. **Pass:** its ring turns orange and thick, and the NPC behind it is **not** targeted.
2. Click empty sky. **Pass:** marker 2 goes back to a thin white ring.
3. Click marker 3, then press on empty space and drag to turn the camera. **Pass:** the camera turns, and marker 3 stays selected.
4. Press on marker 3 and drag. **Pass:** the camera does not turn, and marker 3 is still selected afterwards.
5. With a marker selected, click a button in the test window that sits over empty space. **Pass:** the selection does not change.

If ImGui misses clicks while no plugin window wants the mouse (step 2 fails), stop and report. Don't work around it.

---

### Task 8: The editor keys

*editor § Keys and input: `` ` `` adds to the end, Alt+`` ` `` adds after the selected point (and selects it), Ctrl+`` ` `` overwrites the selected point, and Ctrl+Z / Ctrl+Y undo and redo. None of them work while typing, and all are hidden from the game.*

**Files:**
- Modify: `src/CinematicCam.Plugin/Editor/EditorKeys.cs`

**Interfaces:**
- Consumes: `CameraSession.AddToEnd`, `AddAfterSelected`, `OverwriteSelected`, `Undo` and `Redo` (Task 5); `PhysicalKeys` (Task 5).
- Produces: `EditorKeys.Update(CameraSession session)` acts on each key's press edge. R is added in Task 9.

- [ ] **Step 1: Bind the keys**

Replace `EditorKeys` with:

```csharp
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Editor;

/// <summary>The editing-mode key bindings, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey[] Watched = [VirtualKey.C, VirtualKey.OEM_3, VirtualKey.Z, VirtualKey.Y];

    private readonly bool[] held = new bool[Watched.Length];

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session)
    {
        if (session.Mode != CameraMode.Editing || PhysicalKeys.IsTyping()) { Array.Clear(held); return; }

        var ctrl = PhysicalKeys.IsDown(VirtualKey.CONTROL);
        var alt = PhysicalKeys.IsDown(VirtualKey.MENU);

        for (var i = 0; i < Watched.Length; i++)
        {
            var key = Watched[i];
            var down = PhysicalKeys.IsDown(key);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down) continue;

            var ours = key is VirtualKey.C or VirtualKey.OEM_3 || ctrl;
            if (ours) PhysicalKeys.Hide(key);
            if (pressed && ours) Act(session, key, ctrl, alt);
        }
    }

    private static void Act(CameraSession session, VirtualKey key, bool ctrl, bool alt)
    {
        var refusal = key switch
        {
            VirtualKey.OEM_3 when ctrl => session.OverwriteSelected(),
            VirtualKey.OEM_3 when alt => session.AddAfterSelected(),
            VirtualKey.OEM_3 => session.AddToEnd(),
            VirtualKey.Z => session.Undo() ? null : "Nothing to undo.",
            VirtualKey.Y => session.Redo() ? null : "Nothing to redo.",
            _ => null,
        };

        if (refusal is not null) Plugin.Log.Debug("[editor] {Key}: {Refusal}", key.ToString(), refusal);
    }
}
```

- [ ] **Step 2: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 3: Commit**

```bash
git add src/CinematicCam.Plugin/Editor/EditorKeys.cs
git commit -m "feat(editor) add, overwrite, undo and redo from the keyboard"
```

**In-game check (the user's).** Press **Edit**, then **New track**.
1. Press **`` ` ``** in three places. **Pass:** markers 1, 2 and 3 appear, and nothing else happens in game.
2. Select marker 1, fly somewhere new, and press **Alt+`` ` ``**. **Pass:** a new marker 2 appears between the old 1 and 2, it is selected, and the old 2 and 3 are now 3 and 4.
3. Fly somewhere else and press **Ctrl+`` ` ``**. **Pass:** the selected marker jumps to the camera's position, and its number doesn't change.
4. Press **Ctrl+Z** three times. **Pass:** first the overwrite is undone, then the insert, then the third point. Press **Ctrl+Y** once. **Pass:** the third point is back.
5. Deselect, then press **Alt+`` ` ``** and **Ctrl+`` ` ``**. **Pass:** nothing changes.
6. Open chat and type `` z` ``, then press Ctrl+Z inside the chat box. **Pass:** the text appears and is edited as usual, and no marker appears or disappears.

---

### Task 9: The gizmo

*editor § Gizmo. It sits on the selected point only. Move uses world axes, and Rotate uses yaw, pitch and roll rings in Recorded-aim mode, with only roll in Direction-of-travel mode. R switches between them. A drag commits one undo step on release, and only if the pose changed. The drag doesn't reach the game. The arrows don't flip, and the path follows the drag.*

**Files:**
- Create: `src/CinematicCam.Plugin/Editor/PointGizmo.cs`
- Modify: `src/CinematicCam.Plugin/Editor/EditorLayer.cs`
- Modify: `src/CinematicCam.Plugin/Editor/EditorKeys.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `GizmoMode` and `GizmoEdit.Apply` (Task 4), `PoseMatrix.From` (Part 1), `EditorView` (Task 6), and `CameraSession.ReplacePoint` (Task 5).
- Produces: `internal sealed class PointGizmo` with:
  - `GizmoMode Mode { get; set; }` (the 2b Point window sets it)
  - `void Toggle()`
  - `bool Dragging { get; }`
  - `bool Hot { get; }` (over or using the gizmo as of the last draw)
  - `(int Index, ControlPoint Point)? Preview { get; }`
  - `void Draw(EditorView view, CameraSession session)`
- `EditorKeys.Update(CameraSession session, PointGizmo gizmo)` gains the gizmo parameter.

- [ ] **Step 1: Add `PointGizmo`**

Create `src/CinematicCam.Plugin/Editor/PointGizmo.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGuizmo;

namespace CinematicCam.Plugin.Editor;

/// <summary>The move and rotate gizmo on the selected point; a drag commits on release.</summary>
internal sealed unsafe class PointGizmo
{
    private Matrix4x4 matrix;
    private ControlPoint? dragStart;
    private int dragIndex;

    public GizmoMode Mode { get; set; } = GizmoMode.Move;

    /// <summary>True while a drag is in progress.</summary>
    public bool Dragging => dragStart is not null;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>The point being dragged as it would be if released now, or null.</summary>
    public (int Index, ControlPoint Point)? Preview { get; private set; }

    /// <summary>Switches between Move and Rotate, except mid-drag.</summary>
    public void Toggle()
    {
        if (!Dragging) Mode = Mode == GizmoMode.Move ? GizmoMode.Rotate : GizmoMode.Move;
    }

    /// <summary>Draws the gizmo on the selected point into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, CameraSession session)
    {
        if (session.Selected is not { } index || index >= session.Track.Points.Count)
        {
            Hot = false;
            Preview = null;
            dragStart = null;
            return;
        }

        var point = session.Track.Points[index];
        var aim = session.Track.Aim;
        if (!Dragging) matrix = PoseMatrix.From(point.Position, point.Yaw, point.Pitch, point.Roll);

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);

        var operation = Mode == GizmoMode.Move ? ImGuizmoOperation.Translate
            : aim == AimMode.AimKeys ? ImGuizmoOperation.RotateX | ImGuizmoOperation.RotateY | ImGuizmoOperation.RotateZ
            : ImGuizmoOperation.RotateZ;
        var space = Mode == GizmoMode.Move ? ImGuizmoMode.World : ImGuizmoMode.Local;

        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);

        var usingNow = ImGuizmo.IsUsing();
        Hot = usingNow || ImGuizmo.IsOver();

        if (usingNow)
        {
            if (dragStart is null) { dragStart = point; dragIndex = index; }
            Preview = (dragIndex, GizmoEdit.Apply(dragStart, matrix, Mode, aim));
            return;
        }

        if (dragStart is { } start)
        {
            var edited = GizmoEdit.Apply(start, matrix, Mode, aim);
            dragStart = null;
            Preview = null;
            if (!ReferenceEquals(edited, start) && session.ReplacePoint(dragIndex, edited) is { } refusal)
                Plugin.Log.Warning("[editor] gizmo edit refused: {Refusal}", refusal);
        }
    }
}
```

- [ ] **Step 2: Put the gizmo in the editor window, with the path following the drag**

In `EditorLayer`:
- Add `using CinematicCam.Core.Tracks;` and `using Dalamud.Bindings.ImGuizmo;`.
- Change the constructor to take the gizmo: `public EditorLayer(CameraSession session, PointGizmo gizmo)` and store it in `private readonly PointGizmo gizmo;`.
- In `Draw`, draw the preview track instead of `session.Track`:

```csharp
        var track = gizmo.Preview is { } preview ? TrackEditing.Replace(session.Track, preview.Index, preview.Point) : session.Track;
        var markers = overlay.Draw(view, track, session.Selected);
```

- Take the mouse over the gizmo too:

```csharp
        if (hovered is null && !clicks.HoldingMarker && !gizmo.Hot) flags |= ImGuiWindowFlags.NoInputs;
```

- Inside `if (ImGui.Begin(...))`, before the click handling:

```csharp
            ImGuizmo.BeginFrame();
            gizmo.Draw(view, session);
```

- Pass the gizmo to the click tracker: `clicks.Update(ImGui.IsMouseDown(ImGuiMouseButton.Left), io.MousePos, overUi, gizmo.Hot, hovered)`.

- [ ] **Step 3: Bind R**

In `EditorKeys`: add `VirtualKey.R` to `Watched`, change the signature to `public void Update(CameraSession session, PointGizmo gizmo)`, and pass `gizmo` through to `Act`. R is ours without Ctrl. The ownership line becomes:

```csharp
            var ours = key is VirtualKey.C or VirtualKey.OEM_3 or VirtualKey.R || ctrl;
```

In `Act`, add a case before the default:

```csharp
            VirtualKey.R when session.Selected is not null => Toggle(gizmo),
```

with:

```csharp
    private static string? Toggle(PointGizmo gizmo)
    {
        gizmo.Toggle();
        return null;
    }
```

- [ ] **Step 4: Wire it in**

In `Plugin`: add the field `private readonly PointGizmo pointGizmo = new();`, construct the layer with `new EditorLayer(Session, pointGizmo)`, and change the keys call to `editorKeys.Update(Session, pointGizmo);`.

- [ ] **Step 5: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Plugin/Editor/PointGizmo.cs src/CinematicCam.Plugin/Editor/EditorLayer.cs src/CinematicCam.Plugin/Editor/EditorKeys.cs src/CinematicCam.Plugin/Plugin.cs
git commit -m "feat(editor) move and rotate the selected point with a gizmo"
```

**In-game check (the user's).** Press **Edit** with a four-point track in Recorded-aim mode, and select marker 2.
1. **Pass:** red, green and blue arrows sit on marker 2. Fly round to the far side. **Pass:** the arrows keep their directions and do not flip.
2. Drag the red arrow. **Pass:** the camera does not turn, and marker 2 and the path follow the drag as you move. Release, then press **Ctrl+Z** once. **Pass:** marker 2 goes back where it was.
3. Click the gizmo's arrow without moving the mouse, then press **Ctrl+Z**. **Pass:** the undo takes back the step *before* that click, which shows the click added no step.
4. Press **R**. **Pass:** three rings replace the arrows. Drag the ring that turns the aim line left and right. **Pass:** marker 2's aim line swings to follow.
5. Set Aim to **Direction of travel**. **Pass:** only one ring shows. Drag it, press **Play**, and watch past point 2. **Pass:** the picture rolls there.
6. Press **Edit**, deselect, and press **R**. **Pass:** nothing happens in game.

---

### Task 10: Constant gizmo size (try once)

*editor § Gizmo: the gizmo keeps a constant size on screen, if that's cheap. Probe 1 showed it scaling with distance.*

**Files:**
- Modify: `src/CinematicCam.Plugin/Editor/PointGizmo.cs`

**Interfaces:**
- Consumes: `GizmoScale.ClipSize` (Task 4), and `EditorView.ViewProjection`, `GizmoView`, `GizmoProjection` and `CameraRight` (Task 6).

- [ ] **Step 1: Correct the size before each manipulate**

In `PointGizmo`, add `private const float ScreenSize = 0.1f;` (ImGuizmo's default clip-space size). Then in `Draw`, just before `Manipulate`, add:

```csharp
        var size = GizmoScale.ClipSize(point.Position, view.CameraRight, gizmoView * gizmoProjection, view.ViewProjection, ScreenSize);
        ImGuizmo.SetGizmoSizeClipSpace(size);
        Plugin.Log.Debug("[editor] gizmo clip size {Size:0.0000} at {Distance:0.0} m", size, Vector3.Distance(point.Position, CameraPositionFrom(view)));
```

Declare `gizmoView` and `gizmoProjection` above this block if they aren't already, and add:

```csharp
    private static Vector3 CameraPositionFrom(EditorView view)
        => Matrix4x4.Invert(view.GizmoView, out var inverse) ? inverse.Translation : Vector3.Zero;
```

Log once a second, not every frame. Use the timestamp pattern in `GizmoProbe.LogCameraPositions`.

- [ ] **Step 2: Build and test**

Run: `./build.sh` → 0 errors, 0 warnings. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 3: Commit**

```bash
git add src/CinematicCam.Plugin/Editor/PointGizmo.cs
git commit -m "feat(editor) keep the gizmo a fixed size on screen"
```

**In-game check (the user's).** Raise Dalamud's log level to Debug. Select a point, then fly from about 3 m away to about 30 m away and back.
1. **Pass:** the arrows stay about the same length on screen.
2. Report the logged `clip size` values at the near and far distances, whether or not step 1 passed.

**If step 1 fails**, revert this task's commit (`git revert --no-edit HEAD`). The spec lets the gizmo keep its size in that case. The controller records the logged values in the spec, and the gizmo stays as it is.

---

## After Part 2a

The controller records in the spec whether constant gizmo size landed or was reverted, and any in-game surprises. Then Part 2b's plan is written against the code as built. 2b covers:
- the track editor window: the mode, fly speed, aim, playback and New track controls; the Add split button and menu; the point list with click-to-select, double-click jump, drag-to-reorder and leg and hold fields; the scrub bar; and the Undo and Redo buttons;
- the Point window: position in yalms; yaw, pitch, roll and FoV in degrees, with Yaw and Pitch disabled in Direction-of-travel mode; the Move/Rotate toggle, which sets `PointGizmo.Mode`; Delete; and no close button;
- a place in the UI for the refusal messages that `EditorKeys` currently only logs at Debug;
- scrub and jumps: showing the frame while dragging in editing and flying on from it on release; seeking while live; and writing `DirH`/`DirV` on scrub release, jumps and Edit from Live;
- deleting the three probes, `/ccam probe`, and the test window.
