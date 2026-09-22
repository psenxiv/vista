# Vista Rename and Speed-Driven Timing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the plugin to Vista, then replace hand-typed leg times with speed-driven legs. A track has a speed, legs follow it or are pinned at their own speed, and timing keys are compiled from that.

**Architecture:**
- Task 1 renames everything mechanically.
- Task 2 swaps Core's timing model. `Track` stores a `PointTiming` per point (pinned leg speed, hold, easing sides, broken handles) and a track `Speed`. A new `TimingCompiler` turns that into the `TimingKey` list that `TimingCurve` already understands. `TrackEvaluator` compiles, and exposes the keys and their times. Keys between points go.
- Task 3 adds the session's speed, duration and pin operations.
- Tasks 4 and 5 update the two windows, in parallel.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-timing-model-design.md`, cited as *§ Section*. It sits on `2026-09-22-curve-editor-design.md`, whose timing authoring it replaces.

## Global Constraints

- **After Task 1:**
  - The projects are `src/Vista.Core`, `src/Vista.Plugin` and `tests/Vista.Tests`, with the same-named namespaces.
  - Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`.
  - Keep 0 warnings, and build and test in the foreground only: no background builds and no `until … sleep` loops.
- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only.
- Never create a `Vista.Plugin.Camera` namespace, because it shadows FFXIVClientStructs' `Camera`.
- Doc comments are one line. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Leave the untracked `CHECKLIST.md` alone. Do not push.
- Nullable values passed straight into `Log.*(..., params object[])` raise CS8604. Format them first.
- No backwards compatibility: nothing is saved to disk yet.
- Invalid input is clamped, not refused. Controls that can't act are disabled. Anything still refused is logged with `Plugin.Log.Warning`.
- UI text names keys in words.
- Don't add features the spec doesn't name. Stop and report anything that needs a product decision.
- In-game checks are the user's. The controller writes `CHECKLIST.md` after the last task.

## Decisions this plan relies on

The spec's, all approved by the user on 2026-09-22:
- Pinned legs.
- Point moves keep every leg's speed.
- Keys between points are removed.
- The track Duration includes holds.
- A new track's speed is 2 yalms per second.
- Speeds from 0.01 to 100 yalms per second.
- A leg from 0.1 to 600 s; a shot up to 3600 s; a hold from 0 to 600 s.
- A full rename, with the folder and GitHub last.

Technical rulings made while planning (the cost if wrong is in brackets):

- **Easing and handles live on the point, not the leg.** `PointTiming.In*` is the arrival side, the in side of the leg arriving at the point. `PointTiming.Out*` is the departure side: the out side of the point's hold end if it has a hold, otherwise of its point key. A leg's easing is `(Timing[leg - 1].OutMode, Timing[leg].InMode)`. This makes "a hold keeps the next leg's easing" automatic. [If wrong: none; it's internal.]
- **A pinned leg's speed is stored on the point it arrives at** (`Timing[leg].LegSpeed`), so one list parallel to the points carries everything, and point edits move one list. `Timing[0].LegSpeed` is always null. [If wrong: none.]
- **Key structure is derived without times:** each point gives one key, plus a hold end when `Hold > 0`. Roles, owners and leg bounds need no evaluator. Times come from `TrackEvaluator.Keys`. [If wrong: none.]
- **Track Duration is solved by bisection** on the track speed in log space, over 0.01 to 100. That covers the clamps: each unpinned leg is held to 0.1 to 600 s. [If wrong: a closed form could replace it later; the behaviour is the same.]
- **A hold end dragged in the graph stays at least 0.05 s** after its point key. It can be removed only with Remove hold or the Hold field. [If wrong: a drag could reach 0 and make the key vanish mid-drag.]
- **The final-review fixes to inner-key code** (commits `84675da`, `c98293c` and `4c4012b` on the old names) are superseded where they touched inner keys. `4c4012b`'s leg-selection clearing stays.

## File map (names after Task 1)

| File | Responsibility |
|---|---|
| `src/Vista.Core/Tracks/PointTiming.cs` | new: per-point timing |
| `src/Vista.Core/Tracks/Track.cs` | `Points`, `Timing` (PointTiming), `Speed`, `Aim`, `Playback` |
| `src/Vista.Core/Tracks/TimingCompiler.cs` | new: keys from a track and its leg lengths |
| `src/Vista.Core/Tracks/TrackEvaluator.cs` | compiles; exposes `Keys`, `LegLength`, `LegSeconds`, `PointSeconds`, `LegAt` |
| `src/Vista.Core/Tracks/TrackEditing.cs` | key structure, point edits, speed, duration and pins |
| `src/Vista.Core/Tracks/LegEasing.cs` | presets on `PointTiming` sides |
| `src/Vista.Core/Tracks/TimingEditing.cs` | key modes, handles, broken, the key drag, remove hold |
| `src/Vista.Core/Tracks/KeyRole.cs` | `Point`, `HoldEnd` |
| `src/Vista.Core/Editing/EditLimits.cs` | + speed and shot-duration ranges |
| `src/Vista.Core/Session/SessionState.cs` | model swap; speed, duration and pin operations |
| `src/Vista.Plugin/Ui/TrackEditorWindow.cs` | track Speed and Duration; per-row Duration, Speed, pin |
| `src/Vista.Plugin/Ui/TimingWindow.cs` | inner keys removed; drag on compiled keys |

---

### Task 1: Rename to Vista

**Files:** every project, source, test and config file that names the plugin. Past specs and plans are not rewritten.

- [ ] **Step 1: Move and rename the projects** with `git mv`:
  - `src/CinematicCam.Core` → `src/Vista.Core`, and its csproj → `Vista.Core.csproj`
  - `src/CinematicCam.Plugin` → `src/Vista.Plugin`, its csproj → `Vista.Plugin.csproj`, and the manifest `CinematicCam.json` → `Vista.json`
  - `tests/CinematicCam.Tests` → `tests/Vista.Tests`, and its csproj → `Vista.Tests.csproj`
  - `CinematicCam.sln` → `Vista.sln`

  Update the solution's project entries and paths, both `ProjectReference`s, `.vscode/settings.json` and `build.sh`.
- [ ] **Step 2: Rename identifiers.**
  - Every `namespace CinematicCam.` and `using CinematicCam.` becomes `Vista.`.
  - In the plugin csproj: `AssemblyName` Vista, `RootNamespace` Vista.Plugin.
  - The manifest's `"Name"` becomes `"Vista"`. Leave the Punchline and Description alone.
  - Delete `packages.lock.json` if it names the old projects, and let the build regenerate it. Report if the build does not regenerate it.
- [ ] **Step 3: Rename user-facing text and IDs.**
  - `CommandName = "/vista"`, and the help message says `/vista`.
  - Log prefixes `[ccam]` → `[vista]`.
  - Window IDs `###ccam-…` → `###vista-…`, and `"##ccam-editor"` → `"##vista-editor"`.
  - The track editor's title "Cinematic Cam" → "Vista".
  - The drag payload `CCAM_POINT` → `VISTA_POINT`.
  - Plugin load and unload log text, and any other "Cinematic Cam" or "CinematicCam" in source.
  - Search with `grep -rn -i 'cinematic\|ccam' src tests` until nothing is left except FFXIVClientStructs names.
- [ ] **Step 4: Rename in docs.**
  - `CLAUDE.md`: title "Vista", the project paths and the test command, the `Vista.Plugin.Camera` warning, and "Cinematic Cam" wherever it names the plugin.
  - `docs/dev-setup.md`, `FEATURES.md`, and any README: likewise, including the Dev Tools DLL path.
  - Leave `docs/superpowers/specs/*` and `docs/superpowers/plans/*` as they are. They are history.
- [ ] **Step 5: Verify.** Run `dotnet test tests/Vista.Tests/Vista.Tests.csproj` (all pass, the same count as before) and `./build.sh` (0 warnings). Check that `src/Vista.Plugin/bin/Debug/Vista.dll` and `Vista.json` exist, and report their paths.
- [ ] **Step 6: Commit:** `refactor(repo) rename cinematic cam to vista`

In-game check for the checklist: Dev Tools loads `Vista.dll`, `/vista` opens the editor, and the window is titled Vista.

### Task 2: The speed-driven timing model in Core

**Files:**
- Create: `src/Vista.Core/Tracks/PointTiming.cs`, `src/Vista.Core/Tracks/TimingCompiler.cs`
- Modify: `Track.cs`, `TrackEvaluator.cs`, `TrackEditing.cs`, `LegEasing.cs`, `TimingEditing.cs`, `KeyRole.cs` (drop `Inner`), `Editing/EditLimits.cs`, `Session/SessionState.cs` (only enough to compile and keep its behaviour), and the plugin's `CameraSession.cs`, `Overlay.cs`, `EditorLayer.cs`, `TrackEditorWindow.cs` and `TimingWindow.cs` (only enough to compile; Tasks 4 and 5 do the UI)
- Tests: `TrackEditingTests`, `TimingEditingTests`, `LegEasingTests`, `TrackEvaluatorTests`, `TrackPlaybackTests`, `DirectorTests`, the Session tests, and a new `TimingCompilerTests`

**Interfaces:**
- Produces:

```csharp
/// <summary>A point's timing: the pinned speed of the leg arriving at it, its hold, and its arrival and departure easing.</summary>
public sealed record PointTiming(
    float? LegSpeed = null,
    float Hold = 0f,
    TangentMode InMode = TangentMode.Auto,
    TangentMode OutMode = TangentMode.Auto,
    float InTangent = 0f,
    float OutTangent = 0f,
    bool Broken = false);

/// <summary>A camera move: a path through control points, their timing, the track's speed, and how it aims.</summary>
public sealed record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackMode Playback);
```

  - `TimingCompiler.Compile(Track track, IReadOnlyList<float> legLengths) -> IReadOnlyList<TimingKey>`. `legLengths[leg]` is for legs 1..n-1, and `[0]` is unused.
  - `TimingCompiler.LegDuration(float length, float speed) -> float`, which is `Math.Clamp(length / speed, MinLegSeconds, MaxSeconds)`.
  - `TrackEvaluator`:
    - `IReadOnlyList<TimingKey> Keys`
    - `float LegLength(int leg)`
    - `float LegSeconds(int leg)`
    - `float PointSeconds(int point)`
    - `int? LegAt(float time)`, which returns null inside a hold or outside the shot
    - `Duration` is the compiled last key's time. All the Task 2 queries from 2c-2 stay.
  - `TrackEditing`:
    - Constants: `DefaultSpeed = 2f`, `MinSpeed = 0.01f`, `MaxSpeed = 100f`, `MinLegSeconds = 0.1f`, `MaxSeconds = 600f`, `MaxShotSeconds = 3600f`, `MinKeyGap = 0.05f`.
    - Structure: `KeyCount(Track)`, `RoleOf(Track, int key)`, `PointOf(Track, int key)`, `PointKey(Track, int point)`, `LegStartKey(Track, int leg)`, `LegEndKey(Track, int leg)`.
    - `LegLengths(Track) -> float[]`: index by leg, with `[0] = 0`.
    - `IsPinned(Track, int leg)`, `AllPinned(Track)` (true with fewer than two points), `LegSpeed(Track, int leg)`, `HoldSeconds(Track, int point)`.
    - Edits: `Empty(AimMode = AimKeys)`, `Append`, `InsertAfter`, `Delete`, `Move`, `Replace`, `SetHold`, `SetPlayback`.
    - `SetSpeed(Track, float)`, `SetDuration(Track, float)`, `SetLegDuration(Track, int leg, float)`, `SetLegSpeed(Track, int leg, float)`, `ResetLeg(Track, int leg)`.
    - Removed: `SetLeg`, `MinLegFor`, `LegAt(Track, float)` (now on the evaluator), `RequireValidKeys`, and the inner-key remapping.
  - `LegEasing.Read(Track, int leg)` and `LegEasing.Set(Track, int leg, Easing)`, working on `PointTiming` sides. `Modes` is unchanged.
  - `TimingEditing`:
    - `SetKeyMode(Track, int key, TangentMode)`, `SetHandles(Track, int key, float? inSlope, float? outSlope)`, `SetBroken(Track, int key, bool)` and `HasHandle(Track, int key, KeySide)`.
    - `MoveKey(Track, TrackEvaluator, int key, float time)`.
    - `RemoveHold(Track, int key)`.
    - Removed: `AddInnerKey`, and `DeleteKey` (replaced by `RemoveHold`).
  - `EditLimits.Speed(float)` (0.01–100, NaN becomes the default 2) and `EditLimits.ShotDuration(float)` (0.2–3600, NaN becomes 0.2). `Leg` and `Hold` are unchanged.

- [ ] **Step 1: Write the failing tests.** Use a helper for a straight 3-point track at x = 0, 10, 20. Leg lengths are 10 and 10, and at the default 2 yalms per second the keys fall at 0, 5 and 10 s:

```csharp
    private static ControlPoint P(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);
    private static Track Three() => TrackEditing.Append(TrackEditing.Append(TrackEditing.Append(TrackEditing.Empty(), P(0f)), P(10f)), P(20f));
    private static float[] Times(Track t) => new TrackEvaluator(t).Keys.Select(k => MathF.Round(k.Time, 2)).ToArray();
```

Tests, with the expected values. Compare key times within 0.05 s rather than rounding exactly, since the arc length of collinear Catmull-Rom points can be off by a hair:
- **Compiler and evaluator**
  - `Three()` gives keys `[0, 5, 10]` at positions `[0, 1, 2]`. `Speed` is 2.
  - `SetSpeed(Three(), 5)` gives `[0, 2, 4]`.
  - `SetLegSpeed(Three(), 2, 1)` gives `[0, 5, 15]`. `IsPinned(…, 2)` is true, and `IsPinned(…, 1)` is false.
  - `SetHold(Three(), 1, 3)` gives `[0, 5, 8, 13]` at positions `[0, 1, 1, 2]`. Its key roles are `[Point, Point, HoldEnd, Point]`, and `KeyCount` is 4.
  - With that hold and `LegEasing.Set(…, 2, EaseIn)`, compiled key 2 (the hold end) has `OutMode == Flat`, and compiled key 1 has `OutMode == Auto`.
  - Two points on the same spot compile a 0.1 s leg.
  - A 10 m leg pinned at 0.01 yalms per second compiles to 600 s.
  - `LegSeconds(2)` is 5, `PointSeconds(2)` is 10, `LegLength(1)` is 10 (to 1 decimal), `LegAt(2)` is 1, and `LegAt(6)` inside the hold above is null.
- **Duration**
  - `SetDuration(Three(), 20)` gives `Speed` 1 and `[0, 10, 20]`.
  - Pin leg 2 at 2 (5 s) and give point 1 a 1 s hold. Then `SetDuration(…, 16)` gives `Speed` 1: 16 − 5 − 1 = 10 s for leg 1's 10 m.
  - With every leg pinned, `SetDuration` returns the same instance.
  - `SetDuration(Three(), 0)` gives `Speed` 100 and a duration of 0.2 s.
  - `SetDuration(Three(), 99999)` gives `Speed` 0.01 and a duration of 1200 s: each leg is capped at 600 s.
- **Legs**
  - `SetLegDuration(Three(), 1, 2)` pins leg 1 at 5 and gives `[0, 2, 7]`.
  - `SetLegDuration(…, 1, 0)` pins it at 100, which is 0.1 s.
  - `SetLegSpeed(…, 1, 1000)` pins it at 100.
  - `ResetLeg` clears the pin.
  - `SetSpeed` clamps to 0.01–100.
- **Point edits**
  - `Replace(Three(), 2, P(40))` keeps leg 2 unpinned at speed 2. Its 30 m take 15 s, giving `[0, 5, 20]`.
  - Pin leg 1 at 1, then `InsertAfter(…, 0, P(5))`. Both halves are pinned at 1, giving `[0, 5, 10, 15]`.
  - Set leg 1 to `EaseInOut` and insert inside it. The legs read `EaseIn` and then `EaseOut`.
  - Pin leg 1 at 1 and delete point 1. The merged 20 m leg is pinned at 1, giving `[0, 20]`.
  - Set leg 1 to `EaseIn` and leg 2 to `EaseOut`, then delete point 1. The merged leg reads `EaseInOut`.
  - Deleting point 0 gives `[0, 5]`, and `Timing[0].LegSpeed` is null.
  - Pin leg 1 at 1, give point 2 a 3 s hold, then `Move(…, 2, 0)`. Leg 1's pin and speed stay in slot 1, and the 3 s hold travels with the moved point to slot 0. The key times are `[0, 3, 3 + L1 / 1, … + L2 / 2]` from the evaluator's own `LegLength`s. Assert the positions are `[0, 0, 1, 2]`.
- **Key edits**
  - `SetKeyMode(Three(), 1, Linear)` sets `Timing[1]`'s In and Out to Linear.
  - With a hold on point 1, `SetKeyMode(…, key 1, Flat)` sets only In, and `SetKeyMode(…, key 2, Flat)` sets only Out.
  - `SetHandles(Three(), 1, 0.1f, 0.3f)` sets Timing[1] to In Manual 0.1 and Out Manual 0.3.
  - `SetBroken` round-trips.
  - `HasHandle` with a hold on point 1: key 0 In false and Out true; key 1 Out false; key 2 In false and Out true; key 3 Out false.
  - `RemoveHold` on a hold end sets `Hold` to 0. On a point key it throws `ArgumentException`.
- **The key drag,** passing `new TrackEvaluator(track)`:
  - `MoveKey(Three(), ev, 1, 3)` pins leg 1 at 10/3 and leg 2 at 10/7, giving `[0, 3, 10]`.
  - Key 0 returns the same instance.
  - `MoveKey(…, 2, 12)` pins leg 2 at 10/7, giving `[0, 5, 12]`.
  - With a 3 s hold on point 1 (`[0, 5, 8, 13]`), moving key 1 to 6 pins leg 1 at 10/6 and sets the hold to 2, giving `[0, 6, 8, 13]`.
  - Moving key 2, the hold end, to 9 sets the hold to 4, giving `[0, 5, 9, 14]`.
  - Moving key 1 of `Three()` to −4 gives `[0, 0.1, 10]`, and to 50 gives `[0, 9.9, 10]`.
- **Easing**
  - Migrate the existing `LegEasingTests` to the new model, keeping their assertions.
  - A hold keeps the next leg's easing, which is now automatic. Assert it.

Migrate the other test files:
- Tracks built with explicit key lists (`new Track(points, keys, …)`) become builder calls that give the same key times. For example, `SetLegDuration` gives chosen leg times, and setting `PointTiming` modes gives chosen tangent modes.
- Remove tests of removed behaviour: inner keys, `SetLeg`'s inner-key spread, `MinLegFor`, `AddInnerKey`, `DeleteKey` on inner keys, and the inner-key remapping. List them in your report.
- Keep every assertion about playback, aim, the curve and the session unchanged.

- [ ] **Step 2: Run the tests.** Expected: compile failures.

- [ ] **Step 3: Implement.** The compiler:

```csharp
namespace Vista.Core.Tracks;

/// <summary>Builds a track's timing keys from its leg speeds, holds and easing.</summary>
public static class TimingCompiler
{
    /// <summary>A leg's time at a speed, clamped to the leg range.</summary>
    public static float LegDuration(float length, float speed)
        => Math.Clamp(length / speed, TrackEditing.MinLegSeconds, TrackEditing.MaxSeconds);

    /// <summary>One key per point and one per hold end; <paramref name="legLengths"/> is indexed by leg.</summary>
    public static IReadOnlyList<TimingKey> Compile(Track track, IReadOnlyList<float> legLengths)
    {
        var keys = new List<TimingKey>(track.Points.Count * 2);
        var time = 0f;
        for (var p = 0; p < track.Points.Count; p++)
        {
            var t = track.Timing[p];
            if (p > 0) time += LegDuration(legLengths[p], t.LegSpeed ?? track.Speed);

            var holds = t.Hold > 0f;
            keys.Add(new TimingKey(time, p, t.InMode, holds ? TangentMode.Auto : t.OutMode, t.InTangent, holds ? 0f : t.OutTangent, t.Broken));
            if (!holds) continue;

            time += t.Hold;
            keys.Add(new TimingKey(time, p, TangentMode.Auto, t.OutMode, 0f, t.OutTangent, t.Broken));
        }

        return keys;
    }
}
```

`TrackEvaluator`:
- Build the timing lengths as today. `LegLength(leg)` is `_lengths[leg - 1]`.
- Compile with `legLengths[leg] = _lengths[leg - 1]`, and build the `TimingCurve` from the compiled keys through `ToDistance`.
- `Keys` returns the compiled list.
- `LegSeconds(leg)` is `Keys[LegEndKey].Time - Keys[LegStartKey].Time`. `PointSeconds(p)` is `Keys[PointKey(p)].Time`.
- `LegAt(time)` searches the legs by those times, inclusive at both ends.

`TrackEditing` structure. There's a key per point, plus a hold end when `Hold > 0`:

```csharp
    /// <summary>How many timing keys the track compiles to.</summary>
    public static int KeyCount(Track track) => track.Points.Count + track.Timing.Count(t => t.Hold > 0f);

    /// <summary>Index of point <paramref name="point"/>'s key.</summary>
    public static int PointKey(Track track, int point)
    {
        ValidatePointIndex(track, point, "point");
        var key = 0;
        for (var p = 0; p < point; p++) key += track.Timing[p].Hold > 0f ? 2 : 1;
        return key;
    }
```

- `PointOf(key)` walks the same way. `RoleOf(key)` is `HoldEnd` when the key is its point's second.
- `LegStartKey(leg)` is `PointKey(leg - 1) + (Timing[leg - 1].Hold > 0 ? 1 : 0)`, and `LegEndKey(leg)` is `PointKey(leg)`.
- `LegLengths` builds an `ArcLengthTable` from the points and applies `TrackEvaluator.MinTimingLength`.

Edits (*§ Rules*):
- **`Empty`:** no points, `Speed = DefaultSpeed`.
- **`Append`:** adds the point and a `new PointTiming()`.
- **`InsertAfter(i, point)`:** after the last point it appends. Otherwise it inserts the point at `i + 1` with `new PointTiming(LegSpeed: Timing[i + 1].LegSpeed)`. The old arrival keeps its own timing.
- **`Delete(i)`:**
  - With one point: an empty track, keeping `Speed`.
  - `i == 0`: remove it, and set the new `Timing[0]` to `LegSpeed = null`.
  - The last point: remove it.
  - A middle point: set `Timing[i + 1] = Timing[i + 1] with { LegSpeed = Timing[i].LegSpeed }`, then remove index `i` from both lists.
- **`Move(from, to)`:** reorder the points and carry each point's `Hold` with it. Every other `PointTiming` field stays in its slot. Build it as `slotTiming[s] with { Hold = oldTiming[order[s]].Hold }`, then set `Timing[0].LegSpeed = null`.
- **`Replace`:** points only.
- **`SetHold`:** clamp to 0–600.
- **`SetSpeed`:** clamp to 0.01–100.
- **`SetLegSpeed`:** clamp, and pin.
- **`SetLegDuration(leg, d)`:** `SetLegSpeed(leg, LegLengths[leg] / Math.Clamp(d, MinLegSeconds, MaxSeconds))`.
- **`ResetLeg`:** set `LegSpeed` to null.
- **`SetDuration(D)`:**
  - With no unpinned leg, return the track unchanged.
  - Otherwise clamp `D` to at most `MaxShotSeconds`. `fixed` is the holds plus the pinned legs' `LegDuration`s. `f(v)` is the sum of `LegDuration(len, v)` over the unpinned legs.
  - Bisect `log v` over [log 0.01, log 100] for 60 iterations to find `fixed + f(v) = D`. `f` decreases as `v` grows, so a `D` above `fixed + f(0.01)` gives 0.01, and one below `fixed + f(100)` gives 100.
  - Return `SetSpeed(track, v)`.
- **Every edit that changes a track** returns the same instance when nothing changes, as today.

`LegEasing.Read` compares `(Timing[leg - 1].OutMode, Timing[leg].InMode)`. `Set` writes those two fields with tangents of 0.

`TimingEditing` maps a key to its point with `PointOf` and `RoleOf`:
- **`SetKeyMode`:**
  - On a point key, it sets In, and also Out when the point has no hold.
  - On a hold end, it sets Out.
  - Tangents go to 0, and Manual is refused.
- **`SetHandles`:**
  - A point key's in slope goes to `In`. Its out slope goes to `Out` only when the point has no hold.
  - A hold end's out slope goes to `Out`, and its in slope is ignored.
  - Slopes are clamped at 0 or above, and a non-finite slope becomes 0.
- **`HasHandle`:**
  - In: `key > 0` and the key isn't a hold end.
  - Out: `key < KeyCount - 1` and the key isn't a point key whose point holds.
- **`SetBroken`:** sets `Timing[PointOf(key)].Broken`.
- **`RemoveHold`:** on a hold end, `SetHold(point, 0)`; otherwise it throws.
- **`MoveKey(track, ev, key, time)`:**
  - Key 0 returns the track unchanged.
  - **A point key of point `p`:**
    - Let `prev = ev.Keys[key - 1].Time`. The leg's time is `d1 = time - prev`, which must be 0.1–600.
    - If the point holds, the hold end stays put, and `hold = ev.Keys[key + 1].Time - time` must be 0.05–600.
    - Else, if a next point exists, `d2 = ev.Keys[key + 1].Time - time` must be 0.1–600.
    - Clamp `time` into the intersection of those bounds. If the intersection is empty, return the track unchanged.
    - Pin leg `p` at `LegLength(p) / d1`. Set the hold, or pin leg `p + 1` at `LegLength(p + 1) / d2`. Clamp speeds to 0.01–100.
  - **A hold end:** `hold = time - ev.Keys[key - 1].Time`, clamped to 0.05–600.

`SessionState` and the plugin, the minimum to compile and keep today's behaviour:
- `Duration` becomes `Evaluator.Duration`.
- Key reads go through `Evaluator.Keys`, and structural checks use `TrackEditing.KeyCount`, `RoleOf` and `PointOf`.
- Remove `AddInnerKey`. `DeleteKey` becomes `RemoveHold(int key)`.
- `PreviewKeyMove(int key, float time)` calls `TimingEditing.MoveKey(start, evaluator, key, time)` from the live edit's start.
- In `Collinear`, a key's position comes from `evaluator.Keys[key].Position`.
- `EndLiveEdit` treats a track as unchanged when the points, `Timing` and `Speed` all match.
- Plugin:
  - `CameraSession.JumpToPoint` and `Overlay` use `Evaluator.PointSeconds`.
  - The track editor's Leg field shows `session.Evaluator.LegSeconds(index)` and applies through `ChangeTrack(t => TrackEditing.SetLegDuration(t, index, EditLimits.Leg(v)))`.
  - The Timing window reads keys from `session.Evaluator.Keys`, drops the diamonds, double-click and Delete key, and calls the new `PreviewKeyMove`, and Remove hold calls `RemoveHold`.
  - The `CameraSession` pass-throughs follow the renames.

- [ ] **Step 4: Run the tests and `./build.sh`.** Expected: all tests pass and 0 warnings.
- [ ] **Step 5: Commit:** `feat(timing) drive leg times from speeds and compile the timing keys`

### Task 3: Speed, duration and pins in the session

**Files:** `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`. Test: `tests/Vista.Tests/Session/SessionTimingTests.cs`.

**Interfaces:**
- Produces, on `SessionState`, with pass-throughs on `CameraSession`, each through `ApplyTiming` so each is one undo step, refused outside editing, and keeping the timing selection:
  - `string? SetTrackSpeed(float speed)`, `string? SetTrackDuration(float seconds)`
  - `string? SetLegDuration(int leg, float seconds)`, `string? SetLegSpeed(int leg, float speed)`, `string? ResetLeg(int leg)`
  - The inputs go through `EditLimits` first: `Speed`, `ShotDuration` and `Leg`.

- [ ] **Step 1: Write the failing tests.** On a 3-point editing session (x = 0, 10, 20, keys at 0, 5 and 10 s):
  - `SetTrackSpeed(5)` gives a `Duration` of 4, and one `Undo` restores 10.
  - `SetTrackDuration(20)` gives `Track.Speed` 1.
  - `SetLegDuration(1, 2)` pins leg 1, and `Evaluator.LegSeconds(1)` is 2. `ResetLeg(1)` unpins it, and `LegSeconds(1)` is 5.
  - `SetLegSpeed(2, 10)` gives `LegSeconds(2) == 1`.
  - While live, all five return a refusal and change nothing.
  - A selected leg stays selected through `SetLegDuration`.
- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement,** as one-liners over `ApplyTiming` and the `TrackEditing` functions.
- [ ] **Step 4: Run the tests and `./build.sh`.**
- [ ] **Step 5: Commit:** `feat(session) set track speed, duration and pinned legs`

### Task 4: The track editor's speed, duration and pins

**Files:** `src/Vista.Plugin/Ui/TrackEditorWindow.cs`. It runs in parallel with Task 5, which doesn't touch this file.

*§ UI, Track editor*:
- **Second row,** after Playback: a `PendingField` for **Speed**, labelled "Speed", `%.2f`, 70 px, showing `session.Track.Speed`, and one for **Duration**, labelled "Duration", `%.1f`, 70 px, showing `session.Duration`. They apply through `SetTrackSpeed` and `SetTrackDuration`, and are disabled when `TrackEditing.AllPinned(session.Track)`. Each has a tooltip naming its unit: "Track speed, yalms per second" and "Whole shot, holds included, in seconds".
- **Point list columns:** `#`, **Duration (s)**, **Speed**, **Hold (s)**, a pin column, then the existing trash column.
  - Row 1 shows nothing in Duration, Speed and pin.
  - Every other row shows the leg arriving at it. Duration `%.1f` shows `Evaluator.LegSeconds(i)` and applies with `SetLegDuration`. Speed `%.2f` shows the effective speed, `TrackEditing.LegSpeed(track, i)`, and applies with `SetLegSpeed`.
  - A pinned leg shows `IconButton.Draw($"pin{i}", FontAwesomeIcon.Thumbtack, "Pinned: click to follow the track speed")`, which calls `ResetLeg(i)`. An unpinned leg leaves the cell empty.
- Keep the row-selection, drag-to-reorder, double-click jump and trash behaviour unchanged.
- Report refusals as the window already does.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `./build.sh`** (0 warnings) and the tests.
- [ ] **Step 3: Commit:** `feat(ui) set track speed and duration and pin legs in the track editor`

In-game checks for the checklist:
- The track's Speed and Duration fields are coupled. Changing one updates the other and all unpinned legs.
- A row's Duration and Speed are coupled, and editing either pins the leg.
- The pin resets the leg.
- The track fields grey out when every leg is pinned.

### Task 5: The Timing window on the new model

**Files:** `src/Vista.Plugin/Ui/TimingWindow.cs`. It runs in parallel with Task 4.

*§ UI, Timing window*. Task 2 made it compile. This task finishes the cleanup:
- Remove every remaining trace of inner keys: diamond drawing, `KeyRole.Inner` branches, the double-click add, and Delete key.
- The key menu offers Remove hold (on a hold end), Break handles and Unify handles.
- A point-key drag calls `session.PreviewKeyMove(key, time)`, using the graph frozen at drag start as today, including the last key's extension past the right edge.
- A hold-end drag does the same. Its clamping is in Core.
- The top row's key label reads `"Key: {time:0.00} s"` from `session.Evaluator.Keys`. The leg label and the easing drop-down are unchanged.
- The time axis, playhead, scrubbing, leg selection, handles and Custom are unchanged.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `./build.sh`** (0 warnings) and the tests.
- [ ] **Step 3: Commit:** `refactor(ui) drop keys between points from the timing window`

In-game checks for the checklist:
- Dragging a point's key moves time between its neighbours and pins those legs, and the track editor shows the pin.
- A hold end drag changes the hold.
- There are no diamonds, and double-clicking the curve adds nothing.

---

## After the last task (controller)

- Record any changes made while building in the spec.
- Write `CHECKLIST.md` from the in-game checks in Tasks 1, 4 and 5, together with the 2c-2 checks that still apply: easing presets, handles, Break and Unify, scrubbing, and holds.
- Give the user the commands to rename the repo folder, and move the assistant's memory directory to match. Ask before running `gh repo rename vista`.
