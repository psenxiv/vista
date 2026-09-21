# Phase 2c-2 Curve Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-leg easing presets and a Timing window with a distance-against-time graph for shaping a shot's pacing: dragging keys and handles, and adding keys between points.

**Architecture:** Part 1 is Core only, built test-first. Timing keys get a tangent mode per side. `LegEasing` maps presets to side modes. `TimingEditing` holds the key operations. `TrackEditing` learns to carry inner keys through point edits. `TrackEvaluator` answers timing queries, and `SessionState` gains a timing selection and track-wide live edits. Part 2 adds the pure `TimingGraph` mapping in Core, then the `TimingWindow` in the plugin, drawn with ImGui's draw list.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), `Dalamud.Bindings.ImGui`, `Dalamud.Interface` (windowing, `ImRaii`, FontAwesome).

**Spec:** `docs/superpowers/specs/2026-09-22-curve-editor-design.md`, citations as *§ Section*. It builds on `2026-09-21-editor-design.md` and `2026-09-20-cinematic-cam-design.md`.

## Global Constraints

- `CinematicCam.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `tests/CinematicCam.Tests` references Core only.
- Namespaces match folders. Never create a `CinematicCam.Plugin.Camera` namespace.
- Build the plugin with `./build.sh`, never bare `dotnet build`, and keep it at 0 warnings. Tests: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`. **Build and test in the foreground**: no background builds and no `until … sleep` wait loops.
- Doc comments are one line, stating what a thing is or does. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no trailing period, **no body and no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Work and commit on `main`. Stage only your own files, never `git add -A`. Do not push.
- Nullable values passed straight into `Log.*(..., params object[])` raise CS8604. Format them first.
- No backwards compatibility: the plugin is unreleased and nothing is saved to disk.
- Invalid input is prevented, not reported. Controls that can't act are disabled, and drags clamp. Anything still refused is logged with `Plugin.Log.Warning`.
- UI text names keys in words ("Ctrl + Z").
- Do not add features the spec doesn't name. If something needs a product decision, stop and report it.
- If the compiler rejects an ImGui call, find the matching overload under `~/code/Dalamud/imgui/Dalamud.Bindings.ImGui` and report the substitution.
- In-game checks are the user's. The controller writes one `CHECKLIST.md` after the last task. Implementers don't run the game.

## Decisions this plan relies on

Made by the user on 2026-09-22 and recorded in the spec: presets plus a graph; the graph in its own window; presets in the Timing window only; distance against time; point-key drags squeeze their neighbours; inner keys are remapped on point edits, not cleared; no in-game check or temporary UI for Part 1.

Technical rulings made while planning (the cost if wrong is in brackets):

- **Manual tangents are stored in control-point units per second**, as today, and converted to distance per second only for the graph. This keeps a hand-shaped leg's pace when a point moves. [If wrong: store distance slopes instead; only `TimingEditing` and the conversions change.]
- **Unified handles are collinear in the graph, not equal in stored units.** A point key's two sides lie on different segments, so the session converts one graph slope into each side's own units. [If wrong: none.]
- **Drags are computed from the track as it was when the drag began,** not accumulated frame to frame, so clamping never drifts. [If wrong: none.]
- **An inner key within 0.05 s of a newly added point's time is removed** by Add after selected, since it can't keep its spacing on either side. This is a small deviation from "kept and remapped" (*§ Point edits and inner keys*), and is reported to the user. [If wrong: nudge it instead.]
- **Keys stay at least 0.05 s apart, and legs at least 0.1 s; each leg and hold is at most 600 s.** The Leg field clamps up to the minimum a leg's inner keys need. [If wrong: none.]
- **The timing selection clears on point-structure edits** (add, delete, reorder, clear), except for a selected point key, which follows its point. [If wrong: keys between points lose selection more often than needed.]
- **Tasks run one at a time.** Parallel implementers would share one working tree and one test project, so each would break the other's build mid-task, and the user rules out worktrees. Reviews of a finished task run in the background while the next task is implemented.

## File map

| File | Responsibility |
|---|---|
| `src/CinematicCam.Core/Tracks/TimingKey.cs` | a mode, a tangent and `Broken` per key, per side |
| `src/CinematicCam.Core/Tracks/KeySide.cs` | new: `In`, `Out` |
| `src/CinematicCam.Core/Tracks/KeyRole.cs` | new: `Point`, `HoldEnd`, `Inner` |
| `src/CinematicCam.Core/Tracks/TimingCurve.cs` | per-side slopes; `SlopeAt`, `SideSlope` |
| `src/CinematicCam.Core/Tracks/TrackEvaluator.cs` | distance and slope queries, unit conversion |
| `src/CinematicCam.Core/Tracks/LegEasing.cs` | new: `Easing` and the presets |
| `src/CinematicCam.Core/Tracks/TrackEditing.cs` | key roles and leg helpers; hold, leg, insert, delete and move carry inner keys and easing |
| `src/CinematicCam.Core/Tracks/TimingEditing.cs` | new: key modes, moves, inner keys, handles |
| `src/CinematicCam.Core/Session/SessionState.cs` | timing selection, timing operations, track-wide live edits |
| `src/CinematicCam.Core/Editing/TimingGraph.cs` | new: graph mapping and handle maths |
| `src/CinematicCam.Plugin/Session/CameraSession.cs` | pass-throughs |
| `src/CinematicCam.Plugin/Ui/PointWindow.cs` | renamed live-edit calls |
| `src/CinematicCam.Plugin/Ui/TimingWindow.cs` | new: the Timing window |
| `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs` | the icon that opens it |
| `src/CinematicCam.Plugin/Editor/EditorColours.cs` | graph colours |
| `src/CinematicCam.Plugin/Plugin.cs` | registers the window |

---

# Part 1: Core

### Task 1: A tangent mode per side

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TimingKey.cs`, `src/CinematicCam.Core/Tracks/TimingCurve.cs`, `src/CinematicCam.Core/Tracks/TrackEditing.cs` (constructor calls only)
- Create: `src/CinematicCam.Core/Tracks/KeySide.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TimingCurveTests.cs`. Migrate every test file that builds a `TimingKey` or reads `.Mode`: `TrackEditingTests`, `DirectorTests`, `TrackEvaluatorTests`, `TrackPlaybackTests`, `SessionStateTests`.

**Interfaces:**
- Produces:
  - `record TimingKey(float Time, float Position, TangentMode InMode = TangentMode.Auto, TangentMode OutMode = TangentMode.Auto, float InTangent = 0f, float OutTangent = 0f, bool Broken = false)`
  - `enum KeySide { In, Out }`
  - `TimingCurve.SlopeAt(double time) -> float`, in position per second
  - `TimingCurve.SideSlope(int index, KeySide side) -> float`, the resolved and clamped slope

- [ ] **Step 1: Change `TimingKey` and add `KeySide`**

```csharp
namespace CinematicCam.Core.Tracks;

/// <summary>A point on the timing curve: at this time the camera is at this place on the path, with a tangent mode on each side.</summary>
public sealed record TimingKey(
    float Time,
    float Position,
    TangentMode InMode = TangentMode.Auto,
    TangentMode OutMode = TangentMode.Auto,
    float InTangent = 0f,
    float OutTangent = 0f,
    bool Broken = false);
```

```csharp
namespace CinematicCam.Core.Tracks;

/// <summary>A timing key's side: In faces the span before it, Out the span after.</summary>
public enum KeySide { In, Out }
```

Update the three constructor calls in `TrackEditing.cs` (lines 20, 93, 127) to `new TimingKey(time, index)`, `new TimingKey(keys[i].Time + seconds, index)` and `new TimingKey(start + (leg * before / (before + after)), index + 1)`.

Migrate the tests. Test helpers such as `TimingCurveTests.Key(time, position, mode, inTangent, outTangent)` become `new(time, position, mode, mode, inTangent, outTangent)`. Replace reads of `.Mode` with `.InMode` or `.OutMode`, as each test means. Don't change what any existing test asserts.

- [ ] **Step 2: Write the failing tests** in `TimingCurveTests.cs`

```csharp
    private static TimingKey Sided(float time, float position, TangentMode inMode, TangentMode outMode, float inTangent = 0f, float outTangent = 0f)
        => new(time, position, inMode, outMode, inTangent, outTangent);

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
```

- [ ] **Step 3: Run the tests.** Expected: the new tests fail to compile (`SlopeAt` and `SideSlope` don't exist). Existing tests compile once migrated.

- [ ] **Step 4: Implement in `TimingCurve.cs`**

Replace the mode switch in `BuildTangents` with a slope per side:

```csharp
        var rawIn = new float[n];
        var rawOut = new float[n];
        for (var k = 0; k < n; k++)
        {
            var key = keys[k];
            var auto = k == 0 ? delta[0]
                : k == n - 1 ? delta[n - 2]
                : InteriorRaw(delta[k - 1], delta[k], h[k - 1], h[k]);
            rawIn[k] = SideRaw(key.InMode, key.InTangent, auto, k > 0 ? delta[k - 1] : 0f);
            rawOut[k] = SideRaw(key.OutMode, key.OutTangent, auto, k < n - 1 ? delta[k] : 0f);
        }
```

```csharp
    /// <summary>One side's slope before the monotone clamp.</summary>
    private static float SideRaw(TangentMode mode, float manual, float auto, float linear) => mode switch
    {
        TangentMode.Auto => auto,
        TangentMode.Linear => linear,
        TangentMode.Flat => 0f,
        TangentMode.Manual => manual,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"unknown tangent mode {mode}"),
    };
```

In `Validate`, check both modes with `Enum.IsDefined`, and throw `ArgumentException($"timing key {i} has a non-finite tangent")` when `InTangent` or `OutTangent` is not finite.

Add:

```csharp
    /// <summary>The resolved slope on one side of key <paramref name="index"/>, in position per second; 0 on a side with no span.</summary>
    public float SideSlope(int index, KeySide side)
    {
        if (_keys.Count < 2) return 0f;
        return side == KeySide.In ? _inTangent[index] : _outTangent[index];
    }

    /// <summary>The curve's slope at <paramref name="time"/>, in position per second; 0 outside the keys.</summary>
    public float SlopeAt(double time)
    {
        if (_keys.Count < 2 || time <= _keys[0].Time || time >= Duration) return 0f;

        var k = FindInterval(time);
        var span = _keys[k + 1].Time - _keys[k].Time;
        var t = (float)((time - _keys[k].Time) / span);
        var t2 = t * t;
        var m0 = _outTangent[k] * span;
        var m1 = _inTangent[k + 1] * span;
        var d = (((6f * t2) - (6f * t)) * _keys[k].Position)
              + (((3f * t2) - (4f * t) + 1f) * m0)
              + (((-6f * t2) + (6f * t)) * _keys[k + 1].Position)
              + (((3f * t2) - (2f * t)) * m1);
        return d / span;
    }
```

- [ ] **Step 5: Run the tests.** Expected: all pass. Then run `./build.sh`. Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Tracks tests/CinematicCam.Tests
git commit -m "feat(timing) give each side of a timing key its own tangent mode"
```

### Task 2: Timing queries on the evaluator

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackEvaluator.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackEvaluatorTests.cs`

**Interfaces:**
- Consumes: `TimingCurve.SlopeAt`, `TimingCurve.SideSlope` (Task 1).
- Produces, all on `TrackEvaluator`, with distance as timing measures it (each segment at least `MinTimingLength`):
  - `float TotalDistance`
  - `float DistanceAt(double time)`
  - `float SlopeAt(double time)`, in distance per second
  - `float SideSlope(int key, KeySide side)`, in distance per second
  - `float DistanceOf(float position)`, control-point units to distance
  - `float PositionOf(float distance)`, distance to control-point units
  - `float ToStoredSlope(float position, KeySide side, float distancePerSecond)`
  - `float FromStoredSlope(float position, KeySide side, float stored)`

- [ ] **Step 1: Write the failing tests**

```csharp
    // Points on a line at x = 0, 10, 20; Linear keys at 0, 5, 10 s.
    private static TrackEvaluator StraightLinear()
    {
        var points = new[] { 0f, 10f, 20f }.Select(x => new ControlPoint(new Vector3(x, 0f, 0f), 0f, 0f, 1f)).ToArray();
        var keys = new[]
        {
            new TimingKey(0f, 0f, TangentMode.Linear, TangentMode.Linear),
            new TimingKey(5f, 1f, TangentMode.Linear, TangentMode.Linear),
            new TimingKey(10f, 2f, TangentMode.Linear, TangentMode.Linear),
        };
        return new TrackEvaluator(new Track(points, keys, AimMode.AimKeys, PlaybackMode.Once));
    }

    [Fact]
    public void DistanceQueriesFollowThePath()
    {
        var evaluator = StraightLinear();
        Assert.Equal(20f, evaluator.TotalDistance, 1);
        Assert.Equal(5f, evaluator.DistanceAt(2.5), 1);
        Assert.Equal(2f, evaluator.SlopeAt(2.5), 1);
        Assert.Equal(15f, evaluator.DistanceOf(1.5f), 1);
        Assert.Equal(1.5f, evaluator.PositionOf(15f), 2);
    }

    [Fact]
    public void SideSlopesAreInDistancePerSecond()
    {
        var evaluator = StraightLinear();
        Assert.Equal(2f, evaluator.SideSlope(1, KeySide.Out), 1);
        Assert.Equal(2f, evaluator.SideSlope(1, KeySide.In), 1);
    }

    [Fact]
    public void StoredSlopesConvertBySegmentLength()
    {
        var evaluator = StraightLinear();
        Assert.Equal(0.2f, evaluator.ToStoredSlope(1f, KeySide.Out, 2f), 2);
        Assert.Equal(2f, evaluator.FromStoredSlope(1f, KeySide.In, 0.2f), 1);
    }

    [Fact]
    public void AOnePointTrackHasNoDistance()
    {
        var point = new ControlPoint(Vector3.Zero, 0f, 0f, 1f);
        var evaluator = new TrackEvaluator(new Track(new[] { point }, new[] { new TimingKey(0f, 0f) }, AimMode.AimKeys, PlaybackMode.Once));
        Assert.Equal(0f, evaluator.TotalDistance);
        Assert.Equal(0f, evaluator.DistanceAt(1.0));
        Assert.Equal(0f, evaluator.PositionOf(3f));
    }
```

These expectations assume each segment of the straight track measures 10. If the arc-length table gives a slightly different length, keep the tolerances and derive the expected values from `TotalDistance`. Report any change.

- [ ] **Step 2: Run the tests.** Expected: compile failure on the new members.

- [ ] **Step 3: Implement**

```csharp
    /// <summary>The path's length as timing measures it, each segment at least <see cref="MinTimingLength"/>.</summary>
    public float TotalDistance => _distances[^1];

    /// <summary>Distance along the path at <paramref name="time"/>.</summary>
    public float DistanceAt(double time) => _curve.PositionAt(time);

    /// <summary>Speed along the path at <paramref name="time"/>, in distance per second.</summary>
    public float SlopeAt(double time) => _curve.SlopeAt(time);

    /// <summary>The resolved slope on one side of timing key <paramref name="key"/>, in distance per second.</summary>
    public float SideSlope(int key, KeySide side) => _curve.SideSlope(key, side);

    /// <summary>Distance along the path of a place in control-point units.</summary>
    public float DistanceOf(float position)
    {
        if (_lengths.Length == 0) return 0f;
        var clamped = Math.Clamp(position, 0f, _lengths.Length);
        var segment = Math.Min((int)MathF.Floor(clamped), _lengths.Length - 1);
        return _distances[segment] + ((clamped - segment) * _lengths[segment]);
    }

    /// <summary>The place in control-point units at <paramref name="distance"/> along the path.</summary>
    public float PositionOf(float distance)
    {
        if (_lengths.Length == 0) return 0f;
        var (segment, fraction) = LocateDistance(distance);
        return segment + fraction;
    }

    /// <summary>A slope on one side of <paramref name="position"/>, from distance per second to stored control points per second.</summary>
    public float ToStoredSlope(float position, KeySide side, float distancePerSecond) => distancePerSecond / SideLength(position, side);

    /// <summary>A slope on one side of <paramref name="position"/>, from stored control points per second to distance per second.</summary>
    public float FromStoredSlope(float position, KeySide side, float stored) => stored * SideLength(position, side);

    /// <summary>The timing length of the segment on one side of a place: the one before it for In at a point, else the one it is in.</summary>
    private float SideLength(float position, KeySide side)
    {
        if (_lengths.Length == 0) return 1f;
        var clamped = Math.Clamp(position, 0f, _lengths.Length);
        var segment = Math.Min((int)MathF.Floor(clamped), _lengths.Length - 1);
        if (side == KeySide.In && clamped - segment == 0f && segment > 0) segment--;
        return _lengths[segment];
    }
```

`_distances` has one entry for a track with fewer than two points, so `TotalDistance` is 0 there. The 1-point `DistanceAt` returns the lone key's position, which `ToDistance` leaves at 0.

- [ ] **Step 4: Run the tests.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TrackEvaluator.cs tests/CinematicCam.Tests/Tracks/TrackEvaluatorTests.cs
git commit -m "feat(timing) answer distance and slope queries on the evaluator"
```

### Task 3: Key roles, easing presets and holds that keep easing

**Files:**
- Create: `src/CinematicCam.Core/Tracks/KeyRole.cs`, `src/CinematicCam.Core/Tracks/LegEasing.cs`
- Modify: `src/CinematicCam.Core/Tracks/TrackEditing.cs`
- Test: `tests/CinematicCam.Tests/Tracks/LegEasingTests.cs` (new), `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Consumes: `TimingKey` with sides (Task 1).
- Produces:
  - `enum KeyRole { Point, HoldEnd, Inner }`
  - `TrackEditing.MinKeyGap = 0.05f`, `TrackEditing.MinLegSeconds = 0.1f`, `TrackEditing.MaxSeconds = 600f`
  - `TrackEditing.RoleOf(Track, int key) -> KeyRole`
  - `TrackEditing.PointKey(Track, int point) -> int`, the point's first key
  - `TrackEditing.LegStartKey(Track, int leg) -> int`, `TrackEditing.LegEndKey(Track, int leg) -> int`
  - `TrackEditing.LegAt(Track, float time) -> int?`
  - `enum Easing { Smooth, Linear, EaseIn, EaseOut, EaseInOut, Custom }`
  - `LegEasing.Modes(Easing) -> (TangentMode Out, TangentMode In)`, `LegEasing.Read(Track, int leg) -> Easing`, `LegEasing.Set(Track, int leg, Easing) -> Track`
  - `SetHold` moves the out side between the point key and the hold end (*§ Easing presets*)

- [ ] **Step 1: Write the failing tests.** In `LegEasingTests.cs`, build a 3-point track at x = 0, 10, 20 as `TrackEditingTests.Build3PointTrack` does, with keys at 0, 5 and 10 s:

```csharp
    [Theory]
    [InlineData(Easing.Smooth, TangentMode.Auto, TangentMode.Auto)]
    [InlineData(Easing.Linear, TangentMode.Linear, TangentMode.Linear)]
    [InlineData(Easing.EaseIn, TangentMode.Flat, TangentMode.Auto)]
    [InlineData(Easing.EaseOut, TangentMode.Auto, TangentMode.Flat)]
    [InlineData(Easing.EaseInOut, TangentMode.Flat, TangentMode.Flat)]
    public void APresetSetsTheSidesBoundingItsLegAndReadsBack(Easing easing, TangentMode outMode, TangentMode inMode)
    {
        var track = LegEasing.Set(Build3PointTrack(), 2, easing);
        Assert.Equal(outMode, track.Timing[1].OutMode);
        Assert.Equal(inMode, track.Timing[2].InMode);
        Assert.Equal(easing, LegEasing.Read(track, 2));
        Assert.Equal(Easing.Smooth, LegEasing.Read(track, 1));
    }

    [Fact]
    public void NewLegsAreSmooth() => Assert.Equal(Easing.Smooth, LegEasing.Read(Build3PointTrack(), 1));

    [Fact]
    public void AManualSideReadsCustom()
    {
        var track = Build3PointTrack();
        var timing = track.Timing.ToList();
        timing[1] = timing[1] with { OutMode = TangentMode.Manual, OutTangent = 0.1f };
        Assert.Equal(Easing.Custom, LegEasing.Read(track with { Timing = timing }, 2));
    }

    [Fact]
    public void SettingTheSameEasingReturnsTheSameTrack()
    {
        var track = Build3PointTrack();
        Assert.Same(track, LegEasing.Set(track, 1, Easing.Smooth));
    }

    [Fact]
    public void CustomCannotBeSet() => Assert.Throws<ArgumentOutOfRangeException>(() => LegEasing.Set(Build3PointTrack(), 1, Easing.Custom));

    [Fact]
    public void EasingNeverChangesTimes()
    {
        var track = LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut);
        Assert.Equal(new[] { 0f, 5f, 10f }, track.Timing.Select(k => k.Time));
    }
```

In `TrackEditingTests.cs`:

```csharp
    [Fact]
    public void AddingAHoldMovesTheNextLegsEasingOntoTheHoldEnd()
    {
        var track = LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn);
        track = TrackEditing.SetHold(track, 1, 2f);

        Assert.Equal(TangentMode.Auto, track.Timing[1].OutMode);
        Assert.Equal(TangentMode.Flat, track.Timing[2].OutMode);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void RemovingAHoldMovesTheEasingBack()
    {
        var track = TrackEditing.SetHold(LegEasing.Set(Build3PointTrack(), 2, Easing.EaseIn), 1, 2f);
        track = TrackEditing.SetHold(track, 1, 0f);

        Assert.Equal(3, track.Timing.Count);
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 2));
    }

    [Fact]
    public void KeyRolesTellPointsHoldEndsAndInnerKeysApart()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var timing = track.Timing.ToList();
        timing.Insert(1, new TimingKey(2.5f, 0.5f));
        track = track with { Timing = timing };

        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 0));
        Assert.Equal(KeyRole.Inner, TrackEditing.RoleOf(track, 1));
        Assert.Equal(KeyRole.Point, TrackEditing.RoleOf(track, 2));
        Assert.Equal(KeyRole.HoldEnd, TrackEditing.RoleOf(track, 3));
        Assert.Equal(2, TrackEditing.PointKey(track, 1));
        Assert.Equal(3, TrackEditing.LegStartKey(track, 2));
        Assert.Equal(4, TrackEditing.LegEndKey(track, 2));
    }

    [Fact]
    public void LegAtFindsTheLegAndSkipsHolds()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys at 0, 5, 7, 12
        Assert.Equal(1, TrackEditing.LegAt(track, 2f));
        Assert.Null(TrackEditing.LegAt(track, 6f));
        Assert.Equal(2, TrackEditing.LegAt(track, 9f));
        Assert.Null(TrackEditing.LegAt(track, 13f));
    }
```

- [ ] **Step 2: Run the tests.** Expected: compile failure.

- [ ] **Step 3: Implement.** `KeyRole.cs`:

```csharp
namespace CinematicCam.Core.Tracks;

/// <summary>What a timing key is: a point's arrival, the end of its hold, or a key between points.</summary>
public enum KeyRole { Point, HoldEnd, Inner }
```

In `TrackEditing.cs`:

```csharp
    /// <summary>Keys stay at least this many seconds apart.</summary>
    public const float MinKeyGap = 0.05f;

    /// <summary>The shortest leg, in seconds.</summary>
    public const float MinLegSeconds = 0.1f;

    /// <summary>The longest leg or hold, in seconds.</summary>
    public const float MaxSeconds = 600f;

    /// <summary>What timing key <paramref name="key"/> is.</summary>
    public static KeyRole RoleOf(Track track, int key)
    {
        var position = track.Timing[key].Position;
        if (position != MathF.Floor(position)) return KeyRole.Inner;
        return key > 0 && track.Timing[key - 1].Position == position ? KeyRole.HoldEnd : KeyRole.Point;
    }

    /// <summary>Index of point <paramref name="point"/>'s first key.</summary>
    public static int PointKey(Track track, int point)
    {
        ValidatePointIndex(track, point, "point");
        return FirstKeyIndex(track.Timing, point);
    }

    /// <summary>Index of the key leg <paramref name="leg"/> leaves from: the last key of the point before it.</summary>
    public static int LegStartKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return LastKeyIndex(track.Timing, leg - 1);
    }

    /// <summary>Index of the key leg <paramref name="leg"/> arrives at: its point's first key.</summary>
    public static int LegEndKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return FirstKeyIndex(track.Timing, leg);
    }

    /// <summary>The leg whose time span holds <paramref name="time"/>, or null in a hold or outside the shot.</summary>
    public static int? LegAt(Track track, float time)
    {
        for (var leg = 1; leg < track.Points.Count; leg++)
        {
            if (time >= track.Timing[LegStartKey(track, leg)].Time && time <= track.Timing[LegEndKey(track, leg)].Time) return leg;
        }

        return null;
    }
```

Rewrite `SetHold`'s loop so the out side moves with the hold:

```csharp
        for (var i = 0; i < keys.Count; i++)
        {
            if (hasHold && i == lastIndex)
            {
                if (seconds == 0f) continue;
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            if (i > lastIndex)
            {
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            if (i == firstIndex && hasHold && seconds == 0f)
            {
                result.Add(keys[i] with { OutMode = keys[lastIndex].OutMode, OutTangent = keys[lastIndex].OutTangent });
                continue;
            }

            if (i == firstIndex && !hasHold && seconds > 0f)
            {
                result.Add(keys[i] with { OutMode = TangentMode.Auto, OutTangent = 0f });
                result.Add(new TimingKey(keys[i].Time + seconds, index, OutMode: keys[i].OutMode, OutTangent: keys[i].OutTangent));
                continue;
            }

            result.Add(keys[i]);
        }
```

`LegEasing.cs`:

```csharp
namespace CinematicCam.Core.Tracks;

/// <summary>A leg's easing preset, or Custom when its sides match none.</summary>
public enum Easing { Smooth, Linear, EaseIn, EaseOut, EaseInOut, Custom }

/// <summary>Maps easing presets onto the two sides that bound a leg.</summary>
public static class LegEasing
{
    private static readonly Easing[] Presets = [Easing.Smooth, Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];

    /// <summary>The out mode of a leg's first key and the in mode of its last key for <paramref name="easing"/>.</summary>
    public static (TangentMode Out, TangentMode In) Modes(Easing easing) => easing switch
    {
        Easing.Smooth => (TangentMode.Auto, TangentMode.Auto),
        Easing.Linear => (TangentMode.Linear, TangentMode.Linear),
        Easing.EaseIn => (TangentMode.Flat, TangentMode.Auto),
        Easing.EaseOut => (TangentMode.Auto, TangentMode.Flat),
        Easing.EaseInOut => (TangentMode.Flat, TangentMode.Flat),
        _ => throw new ArgumentOutOfRangeException(nameof(easing), "custom easing has no modes"),
    };

    /// <summary>The preset leg <paramref name="leg"/>'s bounding sides match, or Custom.</summary>
    public static Easing Read(Track track, int leg)
    {
        var sides = (track.Timing[TrackEditing.LegStartKey(track, leg)].OutMode, track.Timing[TrackEditing.LegEndKey(track, leg)].InMode);
        foreach (var preset in Presets)
        {
            if (Modes(preset) == sides) return preset;
        }

        return Easing.Custom;
    }

    /// <summary>Sets leg <paramref name="leg"/>'s bounding sides to <paramref name="easing"/>, leaving times and inner keys alone.</summary>
    public static Track Set(Track track, int leg, Easing easing)
    {
        var (outMode, inMode) = Modes(easing);
        if (Read(track, leg) == easing) return track;

        var start = TrackEditing.LegStartKey(track, leg);
        var end = TrackEditing.LegEndKey(track, leg);
        var timing = track.Timing.ToList();
        timing[start] = timing[start] with { OutMode = outMode, OutTangent = 0f };
        timing[end] = timing[end] with { InMode = inMode, InTangent = 0f };
        return track with { Timing = timing };
    }
}
```

- [ ] **Step 4: Run the tests.** Expected: all pass. The test that builds an inner key by hand doesn't go through `RequireKeyPerPoint`, so it passes before Task 4.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks tests/CinematicCam.Tests/Tracks
git commit -m "feat(timing) add easing presets and keep a leg's easing across holds"
```

### Task 4: Point edits carry inner keys

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackEditing.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Consumes: Task 3's helpers and constants.
- Produces: `SetLeg`, `InsertAfter`, `Delete` and `Move` accept and carry inner keys as the spec's table says (*§ Point edits and inner keys*). `TrackEditing.MinLegFor(Track, int leg) -> float`: the shortest this leg can be, given its inner keys.

- [ ] **Step 1: Write the failing tests.** Add a helper that puts an inner key into a leg by hand:

```csharp
    // Adds a key at the given time and position, keeping keys in time order.
    private static Track WithInner(Track track, float time, float position)
    {
        var timing = track.Timing.Append(new TimingKey(time, position)).OrderBy(k => k.Time).ToList();
        return track with { Timing = timing };
    }

    [Fact]
    public void SetLegSpreadsInnerKeysInProportion()
    {
        var track = WithInner(Build3PointTrack(), 2f, 0.3f);        // leg 1 is 0..5 s
        track = TrackEditing.SetLeg(track, 1, 10f);

        Assert.Equal(new[] { 0f, 4f, 10f, 15f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.3f, track.Timing[1].Position);
    }

    [Fact]
    public void SetLegClampsUpSoInnerKeysKeepTheirSpacing()
    {
        var track = WithInner(Build3PointTrack(), 0.5f, 0.3f);      // spans 0.5 and 4.5 s
        track = TrackEditing.SetLeg(track, 1, 0.1f);

        Assert.Equal(0.5f, TrackEditing.LegSeconds(track, 1), 3);    // the 0.5 s span may only shrink to 0.05 s
        Assert.Equal(TrackEditing.MinLegSeconds, TrackEditing.MinLegFor(Build3PointTrack(), 1));
    }

    [Fact]
    public void AddAfterSelectedSortsInnerKeysIntoTheHalvesByTime()
    {
        // Points at x = 0, 10, 20. Inserting (5, 0, 0) after point 0 splits leg 1 (0..5 s) at 2.5 s, half the distance.
        var track = WithInner(WithInner(Build3PointTrack(), 1f, 0.2f), 4f, 0.8f);
        track = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));

        Assert.Equal(new[] { 0f, 1f, 2.5f, 4f, 5f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.4f, track.Timing[1].Position, 2);
        Assert.Equal(1f, track.Timing[2].Position);
        Assert.Equal(1.6f, track.Timing[3].Position, 2);
        Assert.Equal(2f, track.Timing[4].Position);
        Assert.Equal(3f, track.Timing[5].Position);
    }

    [Fact]
    public void AddAfterSelectedDropsAnInnerKeyTooCloseToTheNewPoint()
    {
        var track = WithInner(Build3PointTrack(), 2.52f, 0.5f);
        track = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));
        Assert.Equal(new[] { 0f, 2.5f, 5f, 10f }, track.Timing.Select(k => k.Time));
    }

    [Fact]
    public void AddAfterSelectedKeepsTheLegsEasingOnItsOuterSides()
    {
        var track = TrackEditing.InsertAfter(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseInOut), 0, Point(5f, 0f, 0f));
        Assert.Equal(Easing.EaseIn, LegEasing.Read(track, 1));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 2));
    }

    [Fact]
    public void DeletingAMiddlePointMergesBothLegsInnerKeysByDistance()
    {
        // Legs 0..5 and 5..10 s, each 10 long; merged, point 1's place sits at half the distance.
        var track = WithInner(WithInner(Build3PointTrack(), 2f, 0.5f), 7f, 1.5f);
        track = TrackEditing.Delete(track, 1);

        Assert.Equal(new[] { 0f, 2f, 7f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(0.25f, track.Timing[1].Position, 2);
        Assert.Equal(0.75f, track.Timing[2].Position, 2);
        Assert.Equal(1f, track.Timing[3].Position);
    }

    [Fact]
    public void DeletingAMiddlePointKeepsTheMergedLegsOuterEasing()
    {
        var track = LegEasing.Set(LegEasing.Set(Build3PointTrack(), 1, Easing.EaseIn), 2, Easing.EaseOut);
        Assert.Equal(Easing.EaseInOut, LegEasing.Read(TrackEditing.Delete(track, 1), 1));
    }

    [Fact]
    public void DeletingAnEndPointTakesItsLegsInnerKeys()
    {
        var first = TrackEditing.Delete(WithInner(Build3PointTrack(), 2f, 0.5f), 0);
        Assert.Equal(new[] { 0f, 5f }, first.Timing.Select(k => k.Time));

        var last = TrackEditing.Delete(WithInner(Build3PointTrack(), 7f, 1.5f), 2);
        Assert.Equal(new[] { 0f, 5f }, last.Timing.Select(k => k.Time));
    }

    [Fact]
    public void ReorderKeepsLegTimesEasingAndInnerKeysInTheirSlots()
    {
        var track = LegEasing.Set(WithInner(Build3PointTrack(), 2f, 0.5f), 1, Easing.EaseOut);
        track = TrackEditing.SetHold(track, 2, 3f);                  // point 2 holds 10..13 s
        track = TrackEditing.Move(track, 2, 0);

        Assert.Equal(new[] { 0f, 3f, 5f, 8f, 13f }, track.Timing.Select(k => k.Time));
        Assert.Equal(new[] { 0f, 0f, 0.5f, 1f, 2f }, track.Timing.Select(k => k.Position));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(track, 1));
        Assert.Equal(new Vector3(20f, 0f, 0f), track.Points[0].Position);
    }
```

Positions in the insert and delete tests depend on arc lengths of collinear points, which should be exact to two decimals. If they aren't, report it rather than loosening the rule.

In the reorder test the moved point keeps its 3 s hold, so the times are: 0 and 3 (its key and hold end), then leg 1's inner key at 3 + 2, then leg 1's end at 3 + 5, then leg 2's end at 8 + 5.

- [ ] **Step 2: Run the tests.** Expected: failures from `RequireKeyPerPoint` ("timing keys between points are not supported yet") and missing `MinLegFor`.

- [ ] **Step 3: Implement.** Replace `RequireKeyPerPoint` with:

```csharp
    /// <summary>Checks each point has one or two keys of its own and every other key lies between the first and last point.</summary>
    private static void RequireValidKeys(Track track)
    {
        var counts = new int[track.Points.Count];
        foreach (var key in track.Timing)
        {
            var whole = MathF.Floor(key.Position);
            if (key.Position != whole)
            {
                if (key.Position <= 0f || key.Position >= track.Points.Count - 1)
                    throw new ArgumentException("a timing key lies outside the track");
                continue;
            }

            var point = (int)whole;
            if (point < 0 || point >= counts.Length) throw new ArgumentException("a timing key lies outside the track");
            counts[point]++;
        }

        if (counts.Any(c => c is < 1 or > 2)) throw new ArgumentException("every point needs one or two timing keys");
    }
```

Add `MinLegFor`, and use it in `SetLeg`:

```csharp
    /// <summary>The shortest leg <paramref name="leg"/> can be while its inner keys stay <see cref="MinKeyGap"/> apart.</summary>
    public static float MinLegFor(Track track, int leg)
    {
        var start = LegStartKey(track, leg);
        var end = LegEndKey(track, leg);
        if (end - start < 2) return MinLegSeconds;

        var keys = track.Timing;
        var smallest = float.MaxValue;
        for (var i = start; i < end; i++) smallest = MathF.Min(smallest, keys[i + 1].Time - keys[i].Time);
        var length = keys[end].Time - keys[start].Time;
        return MathF.Max(MinLegSeconds, length * MinKeyGap / smallest);
    }
```

In `SetLeg`, after validating, raise `seconds` to `MinLegFor(track, index)`, and scale the keys inside the leg:

```csharp
        seconds = MathF.Max(seconds, MinLegFor(track, index));
        var keys = track.Timing;
        var prevLastIndex = LastKeyIndex(keys, index - 1);
        var currFirstIndex = FirstKeyIndex(keys, index);
        var start = keys[prevLastIndex].Time;
        var oldLeg = keys[currFirstIndex].Time - start;
        var diff = seconds - oldLeg;

        var result = new List<TimingKey>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > prevLastIndex && i < currFirstIndex)
                result.Add(keys[i] with { Time = start + ((keys[i].Time - start) * seconds / oldLeg) });
            else
                result.Add(i >= currFirstIndex ? keys[i] with { Time = keys[i].Time + diff } : keys[i]);
        }
```

`InsertAfter` (replace the body after `if (index == track.Points.Count - 1) return Append(track, point);`):

```csharp
        var points = new List<ControlPoint>(track.Points);
        points.Insert(index + 1, point);

        var table = new ArcLengthTable(points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index + 1), TrackEvaluator.MinTimingLength);
        var share = before / (before + after);

        var keys = track.Timing;
        var startIndex = LastKeyIndex(keys, index);
        var endIndex = FirstKeyIndex(keys, index + 1);
        var start = keys[startIndex].Time;
        var time = start + ((keys[endIndex].Time - start) * share);

        var timing = new List<TimingKey>(keys.Count + 1);
        timing.AddRange(keys.Take(startIndex + 1));
        var second = new List<TimingKey>();
        for (var i = startIndex + 1; i < endIndex; i++)
        {
            var key = keys[i];
            if (MathF.Abs(key.Time - time) < MinKeyGap) continue;
            var fraction = key.Position - index;
            if (key.Time < time) timing.Add(key with { Position = index + InsideLeg(fraction / share) });
            else second.Add(key with { Position = index + 1 + InsideLeg((fraction - share) / (1f - share)) });
        }

        timing.Add(new TimingKey(time, index + 1));
        timing.AddRange(second);
        timing.AddRange(keys.Skip(endIndex).Select(k => k with { Position = k.Position + 1 }));
        return track with { Points = points, Timing = timing };
```

```csharp
    /// <summary>A fraction of a leg kept strictly inside it.</summary>
    private static float InsideLeg(float fraction) => Math.Clamp(fraction, 0.001f, 0.999f);
```

`Delete` (after the one-point case):

```csharp
        var keys = track.Timing;
        var n = track.Points.Count;
        var points = new List<ControlPoint>(track.Points);
        points.RemoveAt(index);

        if (index == 0)
        {
            var shift = keys[FirstKeyIndex(keys, 1)].Time;
            return track with { Points = points, Timing = keys.Where(k => k.Position >= 1f).Select(k => k with { Time = k.Time - shift, Position = k.Position - 1 }).ToList() };
        }

        if (index == n - 1)
            return track with { Points = points, Timing = keys.Where(k => k.Position <= n - 2).ToList() };

        var table = new ArcLengthTable(track.Points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index - 1), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var total = before + after;

        var timing = new List<TimingKey>(keys.Count);
        foreach (var key in keys)
        {
            if (key.Position <= index - 1) timing.Add(key);
            else if (key.Position < index) timing.Add(key with { Position = index - 1 + ((key.Position - (index - 1)) * before / total) });
            else if (key.Position == index) continue;
            else if (key.Position < index + 1) timing.Add(key with { Position = index - 1 + ((before + ((key.Position - index) * after)) / total) });
            else timing.Add(key with { Position = key.Position - 1 });
        }

        return track with { Points = points, Timing = timing };
```

`Move` (replace the key-building loop):

```csharp
        var timing = new List<TimingKey>(keys.Count);
        var time = keys[0].Time;
        for (var slot = 0; slot < n; slot++)
        {
            var moved = order[slot];
            var movedFirst = keys[FirstKeyIndex(keys, moved)];
            var movedLastIndex = LastKeyIndex(keys, moved);
            var hasHold = movedLastIndex != FirstKeyIndex(keys, moved);
            var slotFirst = keys[FirstKeyIndex(keys, slot)];
            var slotLast = keys[LastKeyIndex(keys, slot)];

            if (slot > 0)
            {
                var originalStart = keys[LastKeyIndex(keys, slot - 1)].Time;
                foreach (var inner in keys.Where(k => k.Position > slot - 1 && k.Position < slot))
                    timing.Add(inner with { Time = time + (inner.Time - originalStart) });
                time += legs[slot];
            }

            var arrival = movedFirst with { Time = time, Position = slot, InMode = slotFirst.InMode, InTangent = slotFirst.InTangent, Broken = slotFirst.Broken };
            if (!hasHold)
            {
                timing.Add(arrival with { OutMode = slotLast.OutMode, OutTangent = slotLast.OutTangent });
                continue;
            }

            timing.Add(arrival);
            time += keys[movedLastIndex].Time - movedFirst.Time;
            timing.Add(keys[movedLastIndex] with { Time = time, Position = slot, OutMode = slotLast.OutMode, OutTangent = slotLast.OutTangent });
        }
```

Replace every `RequireKeyPerPoint(track)` call with `RequireValidKeys(track)`, and call it from `SetLeg` too. Update or remove existing tests that assert the old "not supported yet" refusal, and say which in your report.

- [ ] **Step 4: Run the tests.** Expected: all pass, old and new.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TrackEditing.cs tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs
git commit -m "feat(timing) carry keys between points through point edits"
```

### Task 5: Key operations

**Files:**
- Create: `src/CinematicCam.Core/Tracks/TimingEditing.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TimingEditingTests.cs`

**Interfaces:**
- Consumes: Task 3's `RoleOf`, `LegAt`, the constants and `SetHold`.
- Produces, all static on `TimingEditing`, each returning the same `Track` instance when nothing changes, and throwing `ArgumentException` or `ArgumentOutOfRangeException` when refused:
  - `SetKeyMode(Track, int key, TangentMode mode) -> Track`: Auto, Linear or Flat on both sides
  - `MoveKey(Track, int key, float time, float position) -> Track`: position is used by inner keys only
  - `AddInnerKey(Track, float time, float position, float slope) -> (Track Track, int Key)`: slope in stored units
  - `DeleteKey(Track, int key) -> Track`
  - `SetHandles(Track, int key, float? inSlope, float? outSlope) -> Track`: stored units; null leaves a side alone
  - `SetBroken(Track, int key, bool broken) -> Track`
  - `HasHandle(Track, int key, KeySide side) -> bool`: a span lies on that side and it isn't a hold

- [ ] **Step 1: Write the failing tests.** Use the 3-point track (keys at 0, 5 and 10 s, positions 0, 1 and 2) and the `WithInner` helper from Task 4:

```csharp
    [Fact]
    public void SetKeyModeSetsBothSidesAndRefusesManual()
    {
        var track = TimingEditing.SetKeyMode(Build3PointTrack(), 1, TangentMode.Linear);
        Assert.Equal((TangentMode.Linear, TangentMode.Linear), (track.Timing[1].InMode, track.Timing[1].OutMode));
        Assert.Throws<ArgumentException>(() => TimingEditing.SetKeyMode(track, 1, TangentMode.Manual));
    }

    [Fact]
    public void MovingAPointKeySqueezesItsNeighboursAndKeepsTheLength()
    {
        var track = TimingEditing.MoveKey(Build3PointTrack(), 1, 3f, 0f);
        Assert.Equal(new[] { 0f, 3f, 10f }, track.Timing.Select(k => k.Time));
        Assert.Equal(1f, track.Timing[1].Position);
    }

    [Fact]
    public void MovingAPointKeyRescalesTheInnerKeysOfBothLegs()
    {
        var track = WithInner(WithInner(Build3PointTrack(), 2f, 0.5f), 7.5f, 1.5f);   // keys 0, 2, 5, 7.5, 10
        track = TimingEditing.MoveKey(track, 2, 4f, 0f);
        Assert.Equal(new[] { 0f, 1.6f, 4f, 7f, 10f }, track.Timing.Select(k => k.Time).Select(t => MathF.Round(t, 3)));
    }

    [Fact]
    public void APointKeyDragClampsToTheShortestLegs()
    {
        var track = TimingEditing.MoveKey(Build3PointTrack(), 1, -4f, 0f);
        Assert.Equal(TrackEditing.MinLegSeconds, track.Timing[1].Time, 4);
        track = TimingEditing.MoveKey(Build3PointTrack(), 1, 50f, 0f);
        Assert.Equal(10f - TrackEditing.MinLegSeconds, track.Timing[1].Time, 4);
    }

    [Fact]
    public void TheFirstKeyNeverMovesAndTheLastKeyChangesTheLength()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TimingEditing.MoveKey(track, 0, 2f, 0f));
        Assert.Equal(12f, TimingEditing.MoveKey(track, 2, 12f, 0f).Timing[2].Time);
    }

    [Fact]
    public void AHoldEndDragChangesTheHoldAndTheNextLeg()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys 0, 5, 7, 12
        track = TimingEditing.MoveKey(track, 2, 8f, 99f);
        Assert.Equal(new[] { 0f, 5f, 8f, 12f }, track.Timing.Select(k => k.Time));
        Assert.Equal(1f, track.Timing[2].Position);
    }

    [Fact]
    public void AnInnerKeyMovesInTimeAndPlaceBetweenItsNeighbours()
    {
        var track = WithInner(Build3PointTrack(), 2f, 0.5f);
        var moved = TimingEditing.MoveKey(track, 1, 3f, 0.7f);
        Assert.Equal((3f, 0.7f), (moved.Timing[1].Time, moved.Timing[1].Position));

        var clamped = TimingEditing.MoveKey(track, 1, 9f, 1.5f);
        Assert.Equal(5f - TrackEditing.MinKeyGap, clamped.Timing[1].Time, 4);
        Assert.True(clamped.Timing[1].Position < 1f);
    }

    [Fact]
    public void AnInnerKeyIsAddedWithUnbrokenManualHandles()
    {
        var (track, key) = TimingEditing.AddInnerKey(Build3PointTrack(), 2f, 0.4f, 0.2f);
        Assert.Equal(1, key);
        Assert.Equal(new TimingKey(2f, 0.4f, TangentMode.Manual, TangentMode.Manual, 0.2f, 0.2f), track.Timing[1]);
    }

    [Fact]
    public void AnInnerKeyCannotGoInAHoldOrTooCloseToAnotherKey()
    {
        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(held, 6f, 1f, 0f));
        Assert.Throws<ArgumentException>(() => TimingEditing.AddInnerKey(Build3PointTrack(), 4.98f, 0.99f, 0f));
    }

    [Fact]
    public void DeletingKeys()
    {
        var inner = WithInner(Build3PointTrack(), 2f, 0.5f);
        Assert.Equal(3, TimingEditing.DeleteKey(inner, 1).Timing.Count);

        var held = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        Assert.Equal(0f, TrackEditing.HoldSeconds(TimingEditing.DeleteKey(held, 2), 1));

        Assert.Throws<ArgumentException>(() => TimingEditing.DeleteKey(inner, 0));
    }

    [Fact]
    public void HandlesSetManualSidesAndBrokenIsKept()
    {
        var track = TimingEditing.SetHandles(Build3PointTrack(), 1, 0.1f, 0.3f);
        Assert.Equal(new TimingKey(5f, 1f, TangentMode.Manual, TangentMode.Manual, 0.1f, 0.3f), track.Timing[1]);

        track = TimingEditing.SetHandles(TimingEditing.SetBroken(track, 1, true), 1, null, 0.5f);
        Assert.Equal((0.1f, 0.5f, true), (track.Timing[1].InTangent, track.Timing[1].OutTangent, track.Timing[1].Broken));
    }

    [Fact]
    public void NegativeHandleSlopesClampToZero()
        => Assert.Equal(0f, TimingEditing.SetHandles(Build3PointTrack(), 1, -2f, null).Timing[1].InTangent);

    [Fact]
    public void HandlesExistOnlyWhereASpanIsNotAHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);   // keys 0, 5, 7, 12
        Assert.False(TimingEditing.HasHandle(track, 0, KeySide.In));
        Assert.True(TimingEditing.HasHandle(track, 0, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 1, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 2, KeySide.In));
        Assert.True(TimingEditing.HasHandle(track, 2, KeySide.Out));
        Assert.False(TimingEditing.HasHandle(track, 3, KeySide.Out));
    }
```

In `MovingAPointKeyRescalesTheInnerKeysOfBothLegs`, moving key 2 from 5 s to 4 s scales leg 1 by 4/5, so its inner key goes from 2 s to 1.6 s. Leg 2 stretches from 5 to 6 s, so its inner key at 2.5 s into the leg goes to 4 + 2.5 × 6/5 = 7 s.

- [ ] **Step 2: Run the tests.** Expected: compile failure.

- [ ] **Step 3: Implement `TimingEditing.cs`**

```csharp
namespace CinematicCam.Core.Tracks;

/// <summary>Edits timing keys directly: their modes, times, handles, and keys between points.</summary>
public static class TimingEditing
{
    /// <summary>How close an inner key may come to its neighbours' places, in control-point units.</summary>
    private const float PositionGap = 1e-4f;

    /// <summary>Sets both sides of key <paramref name="key"/> to Auto, Linear or Flat.</summary>
    public static Track SetKeyMode(Track track, int key, TangentMode mode)
    {
        ValidateKey(track, key);
        if (mode == TangentMode.Manual) throw new ArgumentException("a key's mode can't be set to Manual directly");
        var k = track.Timing[key];
        if (k.InMode == mode && k.OutMode == mode) return track;
        return WithKey(track, key, k with { InMode = mode, OutMode = mode, InTangent = 0f, OutTangent = 0f });
    }

    /// <summary>Moves key <paramref name="key"/> towards <paramref name="time"/>, and an inner key towards <paramref name="position"/>, clamped to keep order and spacing.</summary>
    public static Track MoveKey(Track track, int key, float time, float position)
    {
        ValidateKey(track, key);
        if (key == 0) return track;

        var keys = track.Timing;
        if (TrackEditing.RoleOf(track, key) == KeyRole.Inner)
        {
            var prev = keys[key - 1];
            var next = keys[key + 1];
            var t = Math.Clamp(time, prev.Time + TrackEditing.MinKeyGap, next.Time - TrackEditing.MinKeyGap);
            var low = prev.Position + PositionGap;
            var high = next.Position - PositionGap;
            var p = low <= high ? Math.Clamp(position, low, high) : keys[key].Position;
            return WithKey(track, key, keys[key] with { Time = t, Position = p });
        }

        var before = PreviousAnchor(track, key);
        var after = NextAnchor(track, key);
        var lower = keys[before].Time + MinStretch(track, before, key);
        var upper = after < 0 ? float.MaxValue : keys[after].Time - MinStretch(track, key, after);
        upper = MathF.Min(upper, keys[before].Time + TrackEditing.MaxSeconds);
        if (after >= 0) lower = MathF.Max(lower, keys[after].Time - TrackEditing.MaxSeconds);
        if (lower > upper) return track;

        var target = Math.Clamp(time, lower, upper);
        var old = keys[key].Time;
        if (target == old) return track;

        var timing = keys.ToList();
        var start = keys[before].Time;
        for (var i = before + 1; i < key; i++)
            timing[i] = keys[i] with { Time = start + ((keys[i].Time - start) * (target - start) / (old - start)) };
        timing[key] = keys[key] with { Time = target };
        if (after >= 0)
        {
            var end = keys[after].Time;
            for (var i = key + 1; i < after; i++)
                timing[i] = keys[i] with { Time = target + ((keys[i].Time - old) * (end - target) / (end - old)) };
        }
        else
        {
            for (var i = key + 1; i < keys.Count; i++) timing[i] = keys[i] with { Time = keys[i].Time + (target - old) };
        }

        return track with { Timing = timing };
    }

    /// <summary>Adds an inner key at <paramref name="time"/> and <paramref name="position"/> with unbroken Manual handles at <paramref name="slope"/>, in stored units.</summary>
    public static (Track Track, int Key) AddInnerKey(Track track, float time, float position, float slope)
    {
        if (TrackEditing.LegAt(track, time) is not { } leg) throw new ArgumentException("keys can only be added inside a leg");

        var keys = track.Timing;
        var index = 0;
        while (index < keys.Count && keys[index].Time <= time) index++;
        var prev = keys[index - 1];
        var next = keys[index];
        if (time - prev.Time < TrackEditing.MinKeyGap || next.Time - time < TrackEditing.MinKeyGap)
            throw new ArgumentException("too close to another key");

        var low = MathF.Max(prev.Position, leg - 1) + PositionGap;
        var high = MathF.Min(next.Position, leg) - PositionGap;
        if (low > high) throw new ArgumentException("no room for a key here");

        var s = float.IsFinite(slope) ? MathF.Max(slope, 0f) : 0f;
        var timing = keys.ToList();
        timing.Insert(index, new TimingKey(time, Math.Clamp(position, low, high), TangentMode.Manual, TangentMode.Manual, s, s));
        return (track with { Timing = timing }, index);
    }

    /// <summary>Deletes an inner key, or removes the hold a hold end closes. A point's key can't be deleted.</summary>
    public static Track DeleteKey(Track track, int key)
    {
        ValidateKey(track, key);
        switch (TrackEditing.RoleOf(track, key))
        {
            case KeyRole.Inner:
                var timing = track.Timing.ToList();
                timing.RemoveAt(key);
                return track with { Timing = timing };
            case KeyRole.HoldEnd:
                return TrackEditing.SetHold(track, (int)track.Timing[key].Position, 0f);
            default:
                throw new ArgumentException("a point's key goes with its point");
        }
    }

    /// <summary>Sets Manual slopes, in stored units, on the sides given; a null side is left alone. Negative slopes become 0.</summary>
    public static Track SetHandles(Track track, int key, float? inSlope, float? outSlope)
    {
        ValidateKey(track, key);
        var k = track.Timing[key];
        if (inSlope is { } i) k = k with { InMode = TangentMode.Manual, InTangent = Slope(i) };
        if (outSlope is { } o) k = k with { OutMode = TangentMode.Manual, OutTangent = Slope(o) };
        return k == track.Timing[key] ? track : WithKey(track, key, k);
    }

    /// <summary>Breaks or unifies key <paramref name="key"/>'s handles.</summary>
    public static Track SetBroken(Track track, int key, bool broken)
    {
        ValidateKey(track, key);
        var k = track.Timing[key];
        return k.Broken == broken ? track : WithKey(track, key, k with { Broken = broken });
    }

    /// <summary>True when a span lies on <paramref name="side"/> of key <paramref name="key"/> and it isn't a hold.</summary>
    public static bool HasHandle(Track track, int key, KeySide side)
    {
        ValidateKey(track, key);
        var keys = track.Timing;
        var neighbour = side == KeySide.In ? key - 1 : key + 1;
        return neighbour >= 0 && neighbour < keys.Count && keys[neighbour].Position != keys[key].Position;
    }

    private static float Slope(float value) => float.IsFinite(value) ? MathF.Max(value, 0f) : 0f;

    /// <summary>The nearest point key or hold end before <paramref name="key"/>.</summary>
    private static int PreviousAnchor(Track track, int key)
    {
        var i = key - 1;
        while (TrackEditing.RoleOf(track, i) == KeyRole.Inner) i--;
        return i;
    }

    /// <summary>The nearest point key or hold end after <paramref name="key"/>, or -1 for the last key.</summary>
    private static int NextAnchor(Track track, int key)
    {
        for (var i = key + 1; i < track.Timing.Count; i++)
        {
            if (TrackEditing.RoleOf(track, i) != KeyRole.Inner) return i;
        }

        return -1;
    }

    /// <summary>The shortest the stretch between two anchors may get: a hold's key gap, or a leg's minimum given its inner keys.</summary>
    private static float MinStretch(Track track, int from, int to)
    {
        var keys = track.Timing;
        if (keys[from].Position == keys[to].Position) return TrackEditing.MinKeyGap;
        if (to - from < 2) return TrackEditing.MinLegSeconds;

        var smallest = float.MaxValue;
        for (var i = from; i < to; i++) smallest = MathF.Min(smallest, keys[i + 1].Time - keys[i].Time);
        return MathF.Max(TrackEditing.MinLegSeconds, (keys[to].Time - keys[from].Time) * TrackEditing.MinKeyGap / smallest);
    }

    private static Track WithKey(Track track, int key, TimingKey value)
    {
        var timing = track.Timing.ToList();
        timing[key] = value;
        return track with { Timing = timing };
    }

    private static void ValidateKey(Track track, int key)
    {
        if (key < 0 || key >= track.Timing.Count)
            throw new ArgumentOutOfRangeException(nameof(key), $"key index must be 0..{track.Timing.Count - 1}");
    }
}
```

- [ ] **Step 4: Run the tests.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TimingEditing.cs tests/CinematicCam.Tests/Tracks/TimingEditingTests.cs
git commit -m "feat(timing) add key moves, inner keys and handles"
```

### Task 6: Timing selection and operations in the session

**Files:**
- Modify: `src/CinematicCam.Core/Session/SessionState.cs`, `src/CinematicCam.Plugin/Session/CameraSession.cs`, `src/CinematicCam.Plugin/Ui/PointWindow.cs` (renamed calls only)
- Test: `tests/CinematicCam.Tests/Session/SessionTimingTests.cs` (new), `tests/CinematicCam.Tests/Session/SessionEditingTests.cs` (renamed calls)

**Interfaces:**
- Consumes: Tasks 2, 3 and 5.
- Produces, on `SessionState`, with the same pass-throughs on `CameraSession`:
  - Renamed: `BeginPointEdit` → `BeginLiveEdit`, and `EndPointEdit` → `EndLiveEdit`. `PreviewPoint` is unchanged.
  - `int? SelectedKey`, `int? SelectedLeg`, which are never both set
  - `void SelectKey(int? key)`, `void SelectLeg(int? leg)`
  - `string? SetEasing(int leg, Easing easing)`, `string? SetKeyMode(int key, TangentMode mode)`
  - `string? AddInnerKey(float time)`, which selects the new key; `string? DeleteKey(int key)`, which clears the timing selection
  - `string? BreakHandles(int key)`, `string? UnifyHandles(int key, KeySide from)`
  - `string? PreviewKeyMove(int key, float time, float distance)`, `string? PreviewHandle(int key, KeySide side, float distancePerSecond)`, both during a live edit and computed from the track as it was when the edit began
  - `TrackEvaluator Evaluator`, the evaluator for the current track, shared with `FrameAt`

- [ ] **Step 1: Rename the live edit** in Core, `CameraSession`, `PointWindow` and the tests, including the field `pointEditStart` → `liveEditStart`. Change `EndLiveEdit` so a live edit counts as unchanged only when both points and timing match:

```csharp
        if (start.Track.Points.SequenceEqual(Track.Points) && start.Track.Timing.SequenceEqual(Track.Timing)) Track = start.Track;
        else history.Record(start);
```

Run the tests. Expected: all pass. Commit: `refactor(session) rename the point edit to a live edit`.

- [ ] **Step 2: Write the failing tests** in `SessionTimingTests.cs`. Use a 3-point editing session, as `SessionEditingTests.Editing()` builds one (points at x = 0, 10, 20; keys at 0, 5 and 10 s):

```csharp
    [Fact]
    public void SelectingAPointSelectsItsKeyAndAPointKeySelectsItsPoint()
    {
        var state = Editing();
        state.Select(1);
        Assert.Equal(1, state.SelectedKey);

        state.SelectKey(2);
        Assert.Equal(2, state.Selected);

        state.SelectLeg(1);
        Assert.Equal((null, 1), (state.SelectedKey, state.SelectedLeg));
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void SelectingAnInnerKeyLeavesThePointSelectionAlone()
    {
        var state = Editing();
        state.Select(2);
        Assert.Null(state.AddInnerKey(2f));
        Assert.Equal(1, state.SelectedKey);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void DeselectingAPointClearsItsKeyButNotALeg()
    {
        var state = Editing();
        state.Select(1);
        state.Select(null);
        Assert.Null(state.SelectedKey);

        state.SelectLeg(2);
        state.Select(null);
        Assert.Equal(2, state.SelectedLeg);
    }

    [Fact]
    public void EasingIsOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetEasing(1, Easing.EaseOut));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(state.Track, 1));
        Assert.True(state.Undo());
        Assert.Equal(Easing.Smooth, LegEasing.Read(state.Track, 1));
    }

    [Fact]
    public void AnAddedInnerKeySitsOnTheCurve()
    {
        var state = Editing();
        var before = state.Evaluator.DistanceAt(2.0);
        Assert.Null(state.AddInnerKey(2f));
        Assert.Equal(before, state.Evaluator.DistanceAt(2.0), 1);
    }

    [Fact]
    public void DeletingAKeyClearsTheTimingSelection()
    {
        var state = Editing();
        state.AddInnerKey(2f);
        Assert.Null(state.DeleteKey(1));
        Assert.Null(state.SelectedKey);
        Assert.Equal(3, state.Track.Timing.Count);
    }

    [Fact]
    public void AKeyDragIsOneStepComputedFromTheStart()
    {
        var state = Editing();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewKeyMove(1, 3f, 0f));
        Assert.Null(state.PreviewKeyMove(1, 4f, 0f));
        state.EndLiveEdit();

        Assert.Equal(4f, state.Track.Timing[1].Time);
        Assert.True(state.Undo());
        Assert.Equal(5f, state.Track.Timing[1].Time);
    }

    [Fact]
    public void AnUnbrokenHandleDragKeepsBothSidesCollinearInTheGraph()
    {
        var state = Editing();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewHandle(1, KeySide.Out, 3f));
        state.EndLiveEdit();

        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.In), 1);
        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.Out), 1);
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 1));
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 2));
    }

    [Fact]
    public void ABrokenHandleDragMovesOneSide()
    {
        var state = Editing();
        Assert.Null(state.BreakHandles(1));
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Equal(TangentMode.Auto, state.Track.Timing[1].InMode);
        Assert.Equal(TangentMode.Manual, state.Track.Timing[1].OutMode);
    }

    [Fact]
    public void UnifyGivesBothSidesTheChosenSidesSlope()
    {
        var state = Editing();
        state.BreakHandles(1);
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Null(state.UnifyHandles(1, KeySide.Out));
        Assert.False(state.Track.Timing[1].Broken);
        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.In), 1);
    }

    [Fact]
    public void PointEditsClearAnInnerKeySelection()
    {
        var state = Editing();
        state.AddInnerKey(2f);
        state.AddToEnd(new ControlPoint(new Vector3(30f, 0f, 0f), 0f, 0f, 1f));
        Assert.Null(state.SelectedKey);
    }

    [Fact]
    public void TimingChangesOnlyWhileEditing()
    {
        var state = Editing();
        state.Play();
        Assert.NotNull(state.SetEasing(1, Easing.Linear));
        Assert.NotNull(state.AddInnerKey(2f));
    }
```

`Editing()` here must not select anything, so write it locally rather than reusing a helper that selects.

- [ ] **Step 3: Run the tests.** Expected: compile failure.

- [ ] **Step 4: Implement in `SessionState`**

- Make the cached evaluator public:

```csharp
    /// <summary>The evaluator for the current track, rebuilt when the track changes.</summary>
    public TrackEvaluator Evaluator
    {
        get
        {
            if (!ReferenceEquals(evaluatedTrack, Track))
            {
                evaluator = new TrackEvaluator(Track);
                evaluatedTrack = Track;
            }

            return evaluator!;
        }
    }
```

  `FrameAt` uses `Evaluator.Evaluate(time)`.
- Selection:

```csharp
    /// <summary>The selected timing key's index, or null. Never set together with <see cref="SelectedLeg"/>.</summary>
    public int? SelectedKey { get; private set; }

    /// <summary>The selected leg, or null. Never set together with <see cref="SelectedKey"/>.</summary>
    public int? SelectedLeg { get; private set; }

    /// <summary>Selects a timing key while editing; a point's key also selects its point.</summary>
    public void SelectKey(int? key)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedLeg = null;
        SelectedKey = key is { } k && k >= 0 && k < Track.Timing.Count ? k : null;
        if (SelectedKey is { } s && TrackEditing.RoleOf(Track, s) == KeyRole.Point) Selected = (int)Track.Timing[s].Position;
    }

    /// <summary>Selects a leg while editing, leaving the point selection alone.</summary>
    public void SelectLeg(int? leg)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedKey = null;
        SelectedLeg = leg is { } l && l >= 1 && l < Track.Points.Count ? l : null;
    }
```

  `Select(int? index)` also does `SyncKeyToPoint()`:

```csharp
    /// <summary>Points the timing selection at the selected point's key, or clears a point key's selection when no point is selected.</summary>
    private void SyncKeyToPoint()
    {
        if (Selected is { } point)
        {
            SelectedKey = TrackEditing.PointKey(Track, point);
            SelectedLeg = null;
        }
        else if (SelectedKey is { } key && (key >= Track.Timing.Count || TrackEditing.RoleOf(Track, key) == KeyRole.Point))
        {
            SelectedKey = null;
        }
    }
```

- Point edits clear an inner or hold-end selection. In `Apply`, after setting `Selected`, run:

```csharp
    /// <summary>After a point edit: a point key follows its point, and any other timing selection clears unless it's a leg still in range.</summary>
    private void RefreshTimingSelection()
    {
        if (SelectedKey is { } key && (Selected is null || key >= Track.Timing.Count || TrackEditing.RoleOf(Track, key) != KeyRole.Point)) SelectedKey = null;
        if (Selected is not null && SelectedLeg is null) SelectedKey = TrackEditing.PointKey(Track, Selected.Value);
        if (SelectedLeg is { } leg && leg >= Track.Points.Count) SelectedLeg = null;
    }
```

  Also call it from `Restore`, since undo and redo count as point edits for the selection.
- Timing operations keep the timing selection. Add `ApplyTiming`, which is `Apply` without `RefreshTimingSelection`: it keeps `SelectedKey` if still in range and `SelectedLeg` if still in range. Then:

```csharp
    /// <summary>Sets leg <paramref name="leg"/>'s easing. Returns why it was refused, or null.</summary>
    public string? SetEasing(int leg, Easing easing) => ApplyTiming(t => LegEasing.Set(t, leg, easing));

    /// <summary>Sets both sides of key <paramref name="key"/> to Auto, Linear or Flat. Returns why it was refused, or null.</summary>
    public string? SetKeyMode(int key, TangentMode mode) => ApplyTiming(t => TimingEditing.SetKeyMode(t, key, mode));

    /// <summary>Adds an inner key on the curve at <paramref name="time"/> and selects it. Returns why it was refused, or null.</summary>
    public string? AddInnerKey(float time)
    {
        var evaluator = Evaluator;
        var position = evaluator.PositionOf(evaluator.DistanceAt(time));
        var slope = evaluator.ToStoredSlope(position, KeySide.Out, evaluator.SlopeAt(time));
        var added = -1;
        var refusal = ApplyTiming(t =>
        {
            var (result, key) = TimingEditing.AddInnerKey(t, time, position, slope);
            added = key;
            return result;
        });
        if (refusal is null) { SelectedLeg = null; SelectedKey = added; }
        return refusal;
    }

    /// <summary>Deletes an inner key or a hold end's hold, clearing the timing selection. Returns why it was refused, or null.</summary>
    public string? DeleteKey(int key)
    {
        var refusal = ApplyTiming(t => TimingEditing.DeleteKey(t, key));
        if (refusal is null) SelectedKey = null;
        return refusal;
    }

    /// <summary>Lets key <paramref name="key"/>'s handles move separately. Returns why it was refused, or null.</summary>
    public string? BreakHandles(int key) => ApplyTiming(t => TimingEditing.SetBroken(t, key, true));

    /// <summary>Joins key <paramref name="key"/>'s handles at the slope the <paramref name="from"/> side has in the graph. Returns why it was refused, or null.</summary>
    public string? UnifyHandles(int key, KeySide from)
    {
        var evaluator = Evaluator;
        var slope = evaluator.SideSlope(key, from);
        return ApplyTiming(t => TimingEditing.SetBroken(Collinear(t, evaluator, key, slope, null), key, false));
    }

    /// <summary>During a live edit, moves key <paramref name="key"/> towards a time and, for an inner key, a distance, from the track as the edit began.</summary>
    public string? PreviewKeyMove(int key, float time, float distance)
        => PreviewFromStart((start, evaluator) => TimingEditing.MoveKey(start, key, time, evaluator.PositionOf(distance)));

    /// <summary>During a live edit, sets a handle to a slope in distance per second, both sides unless the key is broken.</summary>
    public string? PreviewHandle(int key, KeySide side, float distancePerSecond)
        => PreviewFromStart((start, evaluator) => start.Timing[key].Broken
            ? Collinear(start, evaluator, key, distancePerSecond, side)
            : Collinear(start, evaluator, key, distancePerSecond, null));
```

```csharp
    /// <summary>Sets Manual slopes from one graph slope on the handled sides: <paramref name="only"/> alone, or both when null.</summary>
    private static Track Collinear(Track track, TrackEvaluator evaluator, int key, float distancePerSecond, KeySide? only)
    {
        var position = track.Timing[key].Position;
        float? Side(KeySide side) => (only is null || only == side) && TimingEditing.HasHandle(track, key, side)
            ? evaluator.ToStoredSlope(position, side, distancePerSecond)
            : null;
        return TimingEditing.SetHandles(track, key, Side(KeySide.In), Side(KeySide.Out));
    }

    private TrackEvaluator? liveStartEvaluator;

    /// <summary>Replaces the track with <paramref name="change"/> of the live edit's starting track. Returns why it was refused, or null.</summary>
    private string? PreviewFromStart(Func<Track, TrackEvaluator, Track> change)
    {
        if (liveEditStart is not { } start) return "No live edit is in progress.";
        if (!ReferenceEquals(evaluatedStart, start.Track))
        {
            liveStartEvaluator = new TrackEvaluator(start.Track);
            evaluatedStart = start.Track;
        }

        try
        {
            var result = change(start.Track, liveStartEvaluator!);
            _ = new TrackEvaluator(result);
            Track = result;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private Track? evaluatedStart;
```

  Put the fields at the top of the class, following the file's order. Every method refuses outside editing with "The track can only change while editing.", through `ApplyTiming`, or through `BeginLiveEdit` not starting.
- `CameraSession`: add one-line pass-throughs for every member listed under Produces.

- [ ] **Step 5: Run the tests.** Expected: all pass. Run `./build.sh`. Expected: 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Session/SessionState.cs src/CinematicCam.Plugin/Session/CameraSession.cs tests/CinematicCam.Tests/Session
git commit -m "feat(session) add timing selection, timing edits and track-wide live edits"
```

---

# Part 2: The Timing window

### Task 7: Graph mapping in Core

**Files:**
- Create: `src/CinematicCam.Core/Editing/TimingGraph.cs`
- Test: `tests/CinematicCam.Tests/Editing/TimingGraphTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct TimingGraph(Vector2 Origin, Vector2 Size, float Duration, float Distance)`, where `Origin` is the plot's top-left in pixels
  - `ToScreen(float time, float distance) -> Vector2`
  - `TimeAt(float x) -> float`, `DistanceAt(float y) -> float`, both clamped to the plot
  - `HandleEnd(Vector2 key, KeySide side, float slope, float length) -> Vector2`
  - `SlopeFromHandle(Vector2 key, KeySide side, Vector2 cursor) -> float`, in distance per second and never negative

- [ ] **Step 1: Write the failing tests**

```csharp
    private static readonly TimingGraph Graph = new(new Vector2(100f, 50f), new Vector2(400f, 200f), 10f, 20f);

    [Fact]
    public void TimeRunsRightAndDistanceRunsUp()
    {
        Assert.Equal(new Vector2(100f, 250f), Graph.ToScreen(0f, 0f));
        Assert.Equal(new Vector2(500f, 50f), Graph.ToScreen(10f, 20f));
        Assert.Equal(new Vector2(300f, 150f), Graph.ToScreen(5f, 10f));
    }

    [Fact]
    public void ScreenPositionsMapBackAndClamp()
    {
        Assert.Equal(5f, Graph.TimeAt(300f), 4);
        Assert.Equal(10f, Graph.DistanceAt(150f), 4);
        Assert.Equal(0f, Graph.TimeAt(0f));
        Assert.Equal(20f, Graph.DistanceAt(0f));
    }

    [Fact]
    public void AHandleLeavesAlongItsSlope()
    {
        // A second is 40 px across and a distance unit 10 px up, so 4 per second is 45 degrees.
        var key = Graph.ToScreen(5f, 10f);
        var end = Graph.HandleEnd(key, KeySide.Out, 4f, 50f);
        Assert.Equal(end.X - key.X, key.Y - end.Y, 3);
        Assert.Equal(50f, Vector2.Distance(key, end), 3);

        var back = Graph.HandleEnd(key, KeySide.In, 4f, 50f);
        Assert.True(back.X < key.X && back.Y > key.Y);
    }

    [Fact]
    public void DraggingAHandleGivesItsSlopeNeverNegative()
    {
        var key = Graph.ToScreen(5f, 10f);
        Assert.Equal(4f, Graph.SlopeFromHandle(key, KeySide.Out, key + new Vector2(40f, -40f)), 3);
        Assert.Equal(4f, Graph.SlopeFromHandle(key, KeySide.In, key + new Vector2(-40f, 40f)), 3);
        Assert.Equal(0f, Graph.SlopeFromHandle(key, KeySide.Out, key + new Vector2(40f, 30f)));
    }
```

- [ ] **Step 2: Run the tests.** Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
using System.Numerics;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>The timing graph's plot: time across, distance up, and the maths for its handles.</summary>
public readonly record struct TimingGraph(Vector2 Origin, Vector2 Size, float Duration, float Distance)
{
    private float SafeDuration => MathF.Max(Duration, 1e-3f);
    private float SafeDistance => MathF.Max(Distance, 1e-3f);

    /// <summary>The pixel for a time and a distance.</summary>
    public Vector2 ToScreen(float time, float distance)
        => new(Origin.X + (time / SafeDuration * Size.X), Origin.Y + Size.Y - (distance / SafeDistance * Size.Y));

    /// <summary>The time under pixel column <paramref name="x"/>, clamped to the shot.</summary>
    public float TimeAt(float x) => Math.Clamp((x - Origin.X) / Size.X, 0f, 1f) * Duration;

    /// <summary>The distance under pixel row <paramref name="y"/>, clamped to the path.</summary>
    public float DistanceAt(float y) => Math.Clamp((Origin.Y + Size.Y - y) / Size.Y, 0f, 1f) * Distance;

    /// <summary>The end of a handle <paramref name="length"/> pixels long leaving <paramref name="key"/> at <paramref name="slope"/> distance per second.</summary>
    public Vector2 HandleEnd(Vector2 key, KeySide side, float slope, float length)
    {
        var direction = Vector2.Normalize(new Vector2(Size.X / SafeDuration, -slope * Size.Y / SafeDistance));
        return key + ((side == KeySide.In ? -direction : direction) * length);
    }

    /// <summary>The slope, in distance per second, of a handle dragged to <paramref name="cursor"/>; never negative.</summary>
    public float SlopeFromHandle(Vector2 key, KeySide side, Vector2 cursor)
    {
        var offset = side == KeySide.In ? key - cursor : cursor - key;
        var across = MathF.Max(offset.X, 1f) / Size.X * SafeDuration;
        var up = MathF.Max(-offset.Y, 0f) / Size.Y * SafeDistance;
        return up / across;
    }
}
```

- [ ] **Step 4: Run the tests.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Editing/TimingGraph.cs tests/CinematicCam.Tests/Editing/TimingGraphTests.cs
git commit -m "feat(timing) map the timing graph to screen"
```

### Task 8: The Timing window: view, selection and scrubbing

**Files:**
- Create: `src/CinematicCam.Plugin/Ui/TimingWindow.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`, `src/CinematicCam.Plugin/Ui/TrackEditorWindow.cs`, `src/CinematicCam.Plugin/Editor/EditorColours.cs`

**Interfaces:**
- Consumes: `CameraSession` pass-throughs (Task 6), `TimingGraph` (Task 7), `MarkerHitTest.Nearest`, `IconButton`.
- Produces: `TimingWindow : Window`, built from `(CameraSession session)`, with `public void Toggle()`. Task 9 adds editing to this class.

Build it to the spec (*§ Timing window*), with these specifics:

- **Registration.** Construct it in `Plugin` after the Point window and add it to `windows`. `TrackEditorWindow` takes it in its constructor, and draws `IconButton.Draw("timing", FontAwesomeIcon.ChartLine, "Timing")` after Redo on the top row, calling `timing.Toggle()` (which sets `IsOpen = !IsOpen`). The icon is never disabled.
- **Window.** Name `"Timing###ccam-timing"`, `RespectCloseHotkey = false`, close button shown (the default). `SizeConstraints` minimum 360 × 200. Dalamud remembers the position.
- **Layout.** A top row, filled in by Task 9; for now it holds only the read-only key time label, `"Key: 7.5 s"` or nothing. Below it, the graph fills the rest of the window. Take the region from `ImGui.GetContentRegionAvail()`. Inset the plot 28 px on the left for point labels, 8 px on top and right, and 22 px at the bottom for the time axis strip. Build `TimingGraph(plotTopLeft, plotSize, (float)session.Duration, session.Evaluator.TotalDistance)` each frame.
- **Input capture.** Cover the whole region with `ImGui.InvisibleButton("##timing-graph", region, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight)`, so the window takes the mouse and the editor layer ignores clicks over it.
- **Empty.** With fewer than two points, draw the centred text "Add two points to shape timing." and stop.
- **Drawing,** on `ImGui.GetWindowDrawList()`, in this order:
  1. The plot's background, `EditorColours.GraphBackground`.
  2. For each point, a horizontal line across the plot at `session.Evaluator.DistanceOf(i)` in `EditorColours.GraphGrid`, and the number `i + 1` right-aligned in the left inset.
  3. Time ticks every whole second along the bottom strip (every 5 s once the shot passes 30 s), labelled `"{t:0}"`, plus the total `"{Duration:0.0} s"` at the right end.
  4. The curve: sample `DistanceAt` at every 2 px across the plot as a polyline, `EditorColours.Path`, 2 px thick.
  5. The selected leg highlighted: the same polyline over its time span in `EditorColours.Selected`.
  6. The playhead: a vertical line at `session.ScrubHead` across the plot and the strip, `EditorColours.Playhead`.
  7. The keys at `graph.ToScreen(key.Time, session.Evaluator.DistanceOf(key.Position))`. A point key is a filled circle of radius 9 with its number in `EditorColours.MarkerText`, on `EditorColours.Marker` with a `MarkerRing` ring. A hold end is a filled circle of radius 5. An inner key is a diamond of half-size 6. The selected key is drawn with `EditorColours.Selected`.
- **Clicks,** only on a left press this frame (`ImGui.IsItemClicked(ImGuiMouseButton.Left)`), with the priority from the spec. The handle slot is filled in by Task 9.
  1. A key within 10 px (`MarkerHitTest.Nearest` over the key screens) → `session.SelectKey(k)`.
  2. Inside the plot, within 8 px vertically of the curve's y at the cursor's time, and `TrackEditing.LegAt` finds a leg → `session.SelectLeg(leg)`.
  3. In the bottom strip → start a scrub: `session.BeginScrub()`, then `session.ScrubTo(graph.TimeAt(x))`. While the button stays active, keep calling `ScrubTo`, and call `session.EndScrub()` on release (`!ImGui.IsItemActive()`). If the window closes mid-scrub, end it in `OnClose`.
- **Outside editing,** clicks 1 and 2 do nothing, since the session's selection methods already refuse. Scrubbing stays available, as it is on the scrub bar.
- **Colours.** In `EditorColours`, add `GraphBackground = 0x60101010`, `GraphGrid = 0x40FFFFFF` and `Playhead = 0xFF4060FF`.

- [ ] **Step 1: Write the window, the colours, the registration and the icon.**
- [ ] **Step 2: Run `./build.sh`.** Expected: 0 warnings, 0 errors. Run the tests. Expected: all pass.
- [ ] **Step 3: Commit:** `feat(ui) add the timing window with the graph, selection and scrubbing`

In-game checks for the controller's checklist: the icon opens and closes the window; the graph shows each point's line, keys and the curve; holds are flat; clicking keys and legs selects them, and a point key also selects the point; the bottom strip scrubs, and the playhead follows Live playback; with fewer than two points the empty message shows.

### Task 9: The Timing window: editing

**Files:**
- Modify: `src/CinematicCam.Plugin/Ui/TimingWindow.cs`

**Interfaces:**
- Consumes: Task 6's session timing operations and `TimingGraph.HandleEnd` / `SlopeFromHandle`.

Add editing (*§ Timing window*), all while `session.Mode == CameraMode.Editing`; otherwise everything below is disabled or ignored:

- **Top row,** left to right:
  - When a leg is selected, `"Leg {leg} → {leg + 1}"` (the numbers shown are 1-based, so leg *l* joins rows *l* and *l* + 1), then a 130 px combo showing `LegEasing.Read`'s name. It lists Smooth, Linear, Ease in, Ease out and Ease in-out, and shows Custom when it reads Custom, which is not selectable. Picking one calls `session.SetEasing`.
  - When a key is selected, `"Key: {time:0.00} s"`, then icon buttons: Smooth (`FontAwesomeIcon.BezierCurve`), Linear (`FontAwesomeIcon.Slash`) and Flat (`FontAwesomeIcon.GripLines`), which call `session.SetKeyMode` with Auto, Linear or Flat. Then a trash icon (`"Delete key"`, or `"Remove hold"` on a hold end), disabled on a point key, which calls `session.DeleteKey`.
  - Each tooltip names the button.
- **Handles.** For the selected key and each side where `TimingEditing.HasHandle` is true, draw a line from the key to `graph.HandleEnd(key, side, session.Evaluator.SideSlope(k, side), 40f)` in `EditorColours.UpLine`, with a filled circle of radius 5 at the end. Handles take priority over keys when clicking, within 8 px.
- **Drags.** On a left press over a handle or a key, call `session.BeginLiveEdit()` and remember what's being dragged. Each frame while the button is active:
  - key: `session.PreviewKeyMove(k, graph.TimeAt(mouse.X), graph.DistanceAt(mouse.Y))`;
  - handle: `session.PreviewHandle(k, side, graph.SlopeFromHandle(keyScreen, side, mouse))`, using the key's screen position from the drag's first frame.

  On release, call `session.EndLiveEdit()`. A refusal (for example after an undo mid-drag) just stops that drag's previews for the rest of the drag. Don't log it. Dragging the first key does nothing (`MoveKey` returns the same track).
- **Double-click** inside the plot, on the curve as for leg clicks: `session.AddInnerKey(graph.TimeAt(mouse.X))`. Log a refusal with `Plugin.Log.Warning`, which covers a double-click on a hold.
- **Right-click** on a key opens a popup `"##timing-key"` for that key, listing:
  - Delete key (inner), or Remove hold (hold end), or nothing for a point key;
  - Break handles, when the key isn't `Broken` and has a handle;
  - Unify handles, when it is `Broken`: `session.UnifyHandles(k, KeySide.Out)`, or `KeySide.In` when the key has no out handle.

  Right-clicking also selects that key.
- Show refusals from these calls as `Plugin.Log.Warning` lines, as the other windows do (`Report`).

- [ ] **Step 1: Write the editing code.**
- [ ] **Step 2: Run `./build.sh`.** Expected: 0 warnings. Run the tests. Expected: all pass.
- [ ] **Step 3: Commit:** `feat(ui) edit timing in the timing window`

In-game checks for the controller's checklist: each preset changes the pace as named and Custom appears after a handle drag; the key buttons; point key and hold end drags squeeze, the first key is fixed, and the last key changes the length; an inner key is added by double-click, dragged in time and distance, and deleted; handles drag together, and separately once broken; unify; each drag is one Ctrl + Z; the graph is read-only while live.

---

## After the last task (controller)

- Record in the spec anything that changed while building.
- Write `CHECKLIST.md` from the in-game checks in Tasks 8 and 9, with falsifiable pass conditions and a Notes line per check, and keys written as words.
- Report the deviations: the inner key dropped near a new point, and anything reviewers changed.
