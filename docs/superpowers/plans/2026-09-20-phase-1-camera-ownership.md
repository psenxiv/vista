# Cinematic Cam Phase 1: Camera Ownership — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take ownership of the FFXIV camera and fly it freely, with an escape
hatch that always works.

**Architecture:** Hook `CameraBase.Update()` (virtual function 3), let the
original run to completion, then overwrite the scene camera's position and
look-at. One hook, one write site, no game camera logic reimplemented. Movement
maths and ownership state live in `CinematicCam.Core` and are unit tested on
macOS; the plugin supplies input and writes memory.

**Tech Stack:** .NET 10, Dalamud 15.0.3.5, FFXIVClientStructs, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`

**Previous phase:** `docs/superpowers/plans/2026-09-20-phase-0-toolchain.md`

## Global Constraints

- `CinematicCam.Core` targets `net10.0`, must never reference Dalamud or
  FFXIVClientStructs, and must not use `unsafe`.
- `CinematicCam.Plugin` targets `net10.0-windows` and is the only project with
  `unsafe`.
- Build only through `./build.sh`; it sets `DALAMUD_HOME`.
- The plugin entry point uses a **parameterless constructor**. Never take
  `IDalamudPluginInterface` as a constructor parameter and never call
  `Create<Plugin>()` — that deadlocks silently on load.
- Doc comments are one line. See `CLAUDE.md`.
- Commit after every task. One-line messages, conventional prefix, no trailers.
  See `CLAUDE.md`.

## Confirmed APIs

These compile against the installed FFXIVClientStructs. Verified 2026-09-20.

```csharp
var manager = CameraManager.Instance();      // FFXIVClientStructs.FFXIV.Client.Game.Control
var camera  = manager->GetActiveCamera();    // Camera*, FFXIVClientStructs.FFXIV.Client.Game
var scene   = &camera->CameraBase.SceneCamera;
scene->Object.Position                       // Vector3, offset 0x50
scene->LookAtVector                          // Vector3, offset 0x80
camera->FoV                                  // float,   offset 0x130
camera->DirH, camera->DirV                   // float, radians
```

## Open finding: direction convention

Measured in-game 2026-09-21, from one sample. **Confirm with a second reading at
a different orientation before relying on it.**

`lookAt - position` normalised gave `<-0.355, -0.922, 0.152>` for
`dirH=1.9749651, dirV=-1.1658802`. Magnitudes match the usual spherical
formula but X and Z are sign-inverted, so the game's convention appears to be:

```
X = -sin(yaw) * cos(pitch)
Y =  sin(pitch)
Z = -cos(yaw) * cos(pitch)
```

Task 7's `FreeCamMotion.Direction` currently uses the un-negated form. If this
holds, it needs the negation or the camera flies backwards. The task 7 test
`LookAtIsOneUnitAheadOfPosition` would still pass either way, since it only
checks distance — add a sign assertion once the convention is confirmed.

## Who runs what

Tasks 1–7 all require **[IN-GAME]** verification by the user. Claude writes the
code, builds, and reads results from
`~/Library/Application Support/XIV on Mac/logs/dalamud.log`.

**Automatic reload does not work under Wine.** Each iteration costs a manual
click on the reload icon in `/xlplugins` -> Dev Tools. Checklists are therefore
written to verify several things per reload rather than one. Tasks 4 and 5 are
designed to share a single game session.

---

## Task 1: Read the camera

Read-only. No hooks, no writes. Confirms the pointers and fields are live before
anything depends on them.

**Files:**
- Create: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Create: `src/CinematicCam.Plugin/SelfTest.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `CinematicCam.Core.CameraState`, `Plugin.Log`.
- Produces:
  - `CameraAccess.TryGetActiveCamera(out Camera* camera)` -> `bool`
  - `CameraAccess.ReadState()` -> `CameraState?`
  - `SelfTest.Run()` -> `void`

- [ ] **Step 1: Write the camera accessor**

`src/CinematicCam.Plugin/Game/CameraAccess.cs`:

```csharp
using CinematicCam.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace CinematicCam.Plugin.Game;

/// <summary>Reads and writes the active game camera.</summary>
internal static unsafe class CameraAccess
{
    public static bool TryGetActiveCamera(out Camera* camera)
    {
        camera = null;
        var manager = CameraManager.Instance();
        if (manager == null) return false;

        camera = manager->GetActiveCamera();
        return camera != null;
    }

    /// <summary>Reads the camera's current position, look-at and field of view.</summary>
    public static CameraState? ReadState()
    {
        if (!TryGetActiveCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new CameraState(scene->Object.Position, scene->LookAtVector, camera->FoV);
    }
}
```

- [ ] **Step 2: Write the self-test**

`src/CinematicCam.Plugin/SelfTest.cs`:

```csharp
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin;

/// <summary>Logs diagnostics that can only be observed in-game.</summary>
internal static unsafe class SelfTest
{
    public static void Run()
    {
        Plugin.Log.Information("[selftest] ---- begin ----");

        if (!CameraAccess.TryGetActiveCamera(out var camera))
        {
            Plugin.Log.Error("[selftest] active camera is null. Aborting.");
            return;
        }

        Plugin.Log.Information("[selftest] camera at 0x{Addr:X}", (nint)camera);

        var vtable = *(nint**)camera;
        Plugin.Log.Information("[selftest] vtable at 0x{Addr:X}", (nint)vtable);
        for (var i = 0; i < 6; i++)
            Plugin.Log.Information("[selftest] vfunc[{Index}] = 0x{Addr:X}", i, vtable[i]);

        var state = CameraAccess.ReadState();
        if (state is null)
        {
            Plugin.Log.Error("[selftest] ReadState returned null despite a valid camera.");
            return;
        }

        Plugin.Log.Information("[selftest] position={Pos} lookAt={Look} fov={Fov} dirH={H} dirV={V}",
            state.Value.Position, state.Value.LookAt, state.Value.Fov, camera->DirH, camera->DirV);

        Plugin.Log.Information("[selftest] ---- end ----");
    }
}
```

- [ ] **Step 3: Wire the command**

In `Plugin.cs`, replace the `selftest` case body with:

```csharp
            case "selftest":
                SelfTest.Run();
                break;
```

Add `using CinematicCam.Plugin.Game;` to the top of `Plugin.cs`.

- [ ] **Step 4: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 5: User runs the in-game checklist**

1. Click reload in `/xlplugins` -> Dev Tools.
2. Stand in an open outdoor area in third person. Run `/ccam selftest`.
3. Rotate the camera about 90 degrees. Run `/ccam selftest` again.
4. Zoom fully in to first person. Run `/ccam selftest` a third time.

- [ ] **Step 6: Claude verifies from the log**

```bash
grep -A 14 "selftest] ---- begin" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -60
```

Check all of these. Stop the phase if any fails:

- Camera address non-zero and identical across all three runs.
- vtable address non-zero; all six vfunc pointers non-zero.
- `position` is plausible world coordinates — FFXIV is typically within about
  ±2000 on X and Z. Not zero, not absurd.
- `position` **changed** between runs 1 and 2. This proves the field tracks the
  live camera rather than a stale copy.
- `fov` is radians, roughly 0.6 to 1.2. If it reads ~45 or ~70 it is degrees and
  every later FoV calculation must account for that.
- `dirH` and `dirV` changed between runs 1 and 2.

Record the value of `vfunc[3]`. Task 2 hooks that address.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(camera) read active camera and log diagnostics"
```

---

## Task 2: Hook Update() as a passthrough

Hook first, change nothing. A passthrough that breaks the game is a hook
problem; one that works proves the plumbing before behaviour rides on it.

**Files:**
- Create: `src/CinematicCam.Plugin/Game/CameraController.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`
- Modify: `src/CinematicCam.Plugin/SelfTest.cs`

**Interfaces:**
- Consumes: `CameraAccess.TryGetActiveCamera`, `Plugin.Hooks`, `Plugin.Log`.
- Produces: `CameraController : IDisposable` with
  `CameraController(Func<CameraState?> stateSource)`, `bool IsHooked`,
  `long UpdateCount`.

- [ ] **Step 1: Write the controller as a counting passthrough**

`src/CinematicCam.Plugin/Game/CameraController.cs`:

```csharp
using CinematicCam.Core;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CinematicCam.Plugin.Game;

/// <summary>Owns the game camera by hooking CameraBase.Update (vfunc 3).</summary>
internal sealed unsafe class CameraController : IDisposable
{
    private const int UpdateVFuncIndex = 3;

    private delegate void CameraUpdateDelegate(CameraBase* camera);

    private readonly Func<CameraState?> stateSource;
    private readonly Hook<CameraUpdateDelegate>? updateHook;

    public bool IsHooked => updateHook?.IsEnabled == true;
    public long UpdateCount { get; private set; }

    public CameraController(Func<CameraState?> stateSource)
    {
        this.stateSource = stateSource;

        if (!CameraAccess.TryGetActiveCamera(out var camera))
        {
            Plugin.Log.Error("[camera] no active camera at construction; hook not installed.");
            return;
        }

        var vtable = *(nint**)camera;
        var updateAddress = vtable[UpdateVFuncIndex];
        Plugin.Log.Information("[camera] hooking Update at 0x{Addr:X}", updateAddress);

        updateHook = Plugin.Hooks.HookFromAddress<CameraUpdateDelegate>(updateAddress, UpdateDetour);
        updateHook.Enable();
    }

    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;
    }

    public void Dispose()
    {
        updateHook?.Disable();
        updateHook?.Dispose();
        Plugin.Log.Information("[camera] hook disposed after {Count} updates.", UpdateCount);
    }
}
```

- [ ] **Step 2: Construct it from the plugin**

In `Plugin.cs`, add the static:

```csharp
    internal static CameraController Camera { get; private set; } = null!;
```

In the constructor, after the `AddHandler` call:

```csharp
        Camera = new CameraController(() => null);
```

First line of `Dispose()`:

```csharp
        Camera.Dispose();
```

- [ ] **Step 3: Report hook health in the self-test**

In `SelfTest.Run()`, immediately before the final `---- end ----` line:

```csharp
        Plugin.Log.Information("[selftest] hooked={Hooked} updateCount={Count}",
            Plugin.Camera.IsHooked, Plugin.Camera.UpdateCount);
```

- [ ] **Step 4: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 5: User runs the in-game checklist**

1. Click reload.
2. Run `/ccam selftest`.
3. Move the camera around normally for about five seconds.
4. Run `/ccam selftest` again.
5. Confirm the camera behaves **completely normally** — rotation, zoom, first
   person, target lock.
6. Change zone, then run `/ccam selftest` a third time.

- [ ] **Step 6: Claude verifies from the log**

```bash
grep -E "\[camera\]|hooked=" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -20
```

Check:

- `hooked=True` on every run.
- `updateCount` rose by thousands between runs 1 and 2, not tens. It should be
  roughly one per frame.
- The user reports no change in camera behaviour.
- After the zone change, `updateCount` is **still climbing**. If it stops, the
  camera object was recreated and the vtable hook no longer applies to the live
  instance. That is a finding, not a bug in this task: record it, and Task 6
  must then re-resolve and re-hook on territory change.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(camera) hook camera update as passthrough"
```

---

## Task 3: Write position and look-at

The first task where the camera moves. Also builds the `hold` and `nudge` verbs
that Tasks 4 and 5 use as probes.

**Files:**
- Modify: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Modify: `src/CinematicCam.Plugin/Game/CameraController.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Produces: `CameraAccess.WriteState(CameraState state)` -> `void`
- Produces: `Plugin.TestState`, a `CameraState?` static. Phase 1 scaffolding,
  removed in phase 2.

- [ ] **Step 1: Add the write method**

Append to `CameraAccess`:

```csharp
    /// <summary>Overwrites camera position and look-at.</summary>
    public static void WriteState(CameraState state)
    {
        if (!TryGetActiveCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = state.Position;
        scene->LookAtVector = state.LookAt;
    }
```

Field of view is deliberately not written here. Task 4 determines whether it can
be.

- [ ] **Step 2: Apply the state in the detour**

Replace `UpdateDetour` in `CameraController`:

```csharp
    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;

        var desired = stateSource();
        if (desired is null) return;

        CameraAccess.WriteState(desired.Value);
    }
```

- [ ] **Step 3: Add the test verbs**

In `Plugin.cs`, add the static:

```csharp
    internal static CameraState? TestState { get; set; }
```

Change the controller construction to:

```csharp
        Camera = new CameraController(() => TestState);
```

Add three cases to `OnCommand`:

```csharp
            case "hold":
            {
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                TestState = current;
                Log.Information("[ccam] holding at {Pos} looking at {Look}",
                    current.Value.Position, current.Value.LookAt);
                break;
            }
            case "release":
                TestState = null;
                Log.Information("[ccam] released.");
                break;
            case "nudge":
            {
                if (TestState is null) { Log.Error("[ccam] nudge requires /ccam hold first."); break; }
                var parts = args.Trim().Split(' ');
                if (parts.Length < 4
                    || !float.TryParse(parts[1], out var dx)
                    || !float.TryParse(parts[2], out var dy)
                    || !float.TryParse(parts[3], out var dz))
                {
                    Log.Error("[ccam] usage: /ccam nudge <dx> <dy> <dz>");
                    break;
                }
                var s = TestState.Value;
                var delta = new Vector3(dx, dy, dz);
                TestState = s with { Position = s.Position + delta, LookAt = s.LookAt + delta };
                Log.Information("[ccam] nudged to {Pos}", TestState.Value.Position);
                break;
            }
```

`nudge` moves position and look-at together so the view translates without
swinging round.

Add to the top of `Plugin.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core;
```

- [ ] **Step 4: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 5: User runs the in-game checklist**

1. Click reload.
2. Stand still in an open outdoor area.
3. Run `/ccam hold`.
4. Try to move the character forward and try to rotate the camera. **Expected:
   the view stays frozen, including while the character walks out of frame.**
5. Run `/ccam nudge 0 5 0`. **Expected: the view rises five yalms, still frozen.**
6. Run `/ccam release`. **Expected: the camera snaps back to normal at once.**

- [ ] **Step 6: Claude verifies**

```bash
grep -E "\[ccam\] (holding|nudged|released)" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -10
```

The decisive evidence is the user's observation at step 4. A frozen view while
the character walks away proves the plugin is the last writer each frame.

If the view drifts or stutters rather than freezing, the game is writing after
us through another path. Record precisely what drifts — position, look
direction, or both — because that determines which additional field needs
writing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(camera) write position and look-at"
```

---

## Task 4: Settle the field-of-view question

Closes the first of the spec's two open questions.

**Files:**
- Modify: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Modify: `src/CinematicCam.Plugin/SelfTest.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`
- Modify: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`

**Interfaces:**
- Produces: `CameraAccess.WriteFov(float fov)` -> `void`
- Produces: `SelfTest.ProbeFov()` -> `void`, logging a line beginning
  `[selftest] FOV VERDICT:`

- [ ] **Step 1: Add the FoV write**

Append to `CameraAccess`:

```csharp
    /// <summary>Writes field of view in radians.</summary>
    public static void WriteFov(float fov)
    {
        if (!TryGetActiveCamera(out var camera)) return;
        camera->FoV = fov;
    }
```

- [ ] **Step 2: Write the probe**

Append to `SelfTest`:

```csharp
    /// <summary>Tests whether a field-of-view write survives the game's camera update.</summary>
    public static void ProbeFov()
    {
        var before = CameraAccess.ReadState();
        if (before is null)
        {
            Plugin.Log.Error("[selftest] FOV VERDICT: cannot read camera.");
            return;
        }

        const float probe = 0.35f;
        var original = before.Value.Fov;
        Plugin.Log.Information("[selftest] fov before write = {Fov}", original);
        CameraAccess.WriteFov(probe);
        Plugin.Log.Information("[selftest] fov immediately after write = {Fov}",
            CameraAccess.ReadState()!.Value.Fov);

        var framesLeft = 10;

        void Check(Dalamud.Plugin.Services.IFramework _)
        {
            var now = CameraAccess.ReadState();
            if (now is null) return;

            framesLeft--;
            if (framesLeft > 0)
            {
                Plugin.Log.Information("[selftest] fov frame -{N} = {Fov}", framesLeft, now.Value.Fov);
                return;
            }

            Plugin.Framework.Update -= Check;

            var survived = MathF.Abs(now.Value.Fov - probe) < 0.001f;
            Plugin.Log.Information(
                "[selftest] FOV VERDICT: {Verdict} (wrote {Probe}, reads {Actual} after 10 frames)",
                survived ? "WRITE SURVIVES" : "OVERWRITTEN BY GAME", probe, now.Value.Fov);

            CameraAccess.WriteFov(original);
            Plugin.Log.Information("[selftest] fov restored to {Fov}", original);
        }

        Plugin.Framework.Update += Check;
    }
```

- [ ] **Step 3: Wire the verb**

In `Plugin.cs` `OnCommand`:

```csharp
            case "fovprobe":
                SelfTest.ProbeFov();
                break;
```

- [ ] **Step 4: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 5: User runs the in-game checklist**

This session also covers Task 5. Do not reload between them if Task 5 is already
built.

1. Click reload.
2. Stand in third person in an open area, camera zoomed out.
3. Run `/ccam fovprobe`. Watch the screen for about one second.
4. Report whether the view visibly zoomed or narrowed at any point, even briefly.
5. Run `/ccam hold`, then `/ccam fovprobe`, then `/ccam release`. Report the
   same observation.

- [ ] **Step 6: Claude reads the verdict**

```bash
grep -E "fov |FOV VERDICT" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -30
```

Three outcomes, each with a defined consequence:

- **WRITE SURVIVES and the user saw the view change.** FoV is directly writable.
  From phase 2, `WriteState` also writes `camera->FoV = state.Fov;`.
- **WRITE SURVIVES but the user saw nothing.** The field is writable but does not
  drive rendering. The projection on `RenderCamera` is the real target. Open a
  follow-up task to probe `RenderCamera->ProjectionMatrix` before phase 2 relies
  on per-point FoV.
- **OVERWRITTEN BY GAME.** The game rewrites FoV after our hook. Move the FoV
  write into the detour alongside position, then re-run this probe.

- [ ] **Step 7: Record the answer in the spec and commit**

Replace the "Known uncertainty" section of the spec with the measured result and
delete the uncertainty wording.

```bash
git add -A
git commit -m "test(camera) probe field of view writability"
```

---

## Task 5: Settle the camera collision question

No new code. A measurement using `hold` and `nudge` from Task 3, and it decides
whether an assembly patch is needed at all.

**Files:**
- Modify: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`

- [ ] **Step 1: User runs the in-game checklist**

Can share a game session with Task 4.

1. Stand directly facing a solid wall or large rock, about 5 yalms away.
2. Run `/ccam hold`.
3. Run `/ccam nudge 0 0 3`, then repeat twice more, driving the camera into and
   through the obstacle.
4. Report: does the view pass through and show the far side, or does it stop,
   get pushed back, or snap elsewhere?
5. Run `/ccam nudge 0 20 0` to rise well above terrain. Report whether the view
   holds altitude.
6. Run `/ccam release`.

Note: the nudge direction is world-space Z, not camera-relative. Face roughly
north or south for step 3 so the nudge drives the camera toward the obstacle,
or adjust the axis to suit the direction you are facing.

- [ ] **Step 2: Claude records the finding**

```bash
grep -E "\[ccam\] nudged" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -10
```

Cross-check the logged positions against what the user saw. The log proves the
plugin asked for the position; the user's report proves whether the game
honoured it.

- **View passes through geometry.** Collision needs no patching. Delete the
  caveat from the spec.
- **View is pushed back or clamped.** A later pass re-clamps position. Record the
  behaviour precisely and open a phase 1b task to locate and patch the collision
  check with our own signature scan.

- [ ] **Step 3: Update the spec and commit**

Replace the "Camera collision may need no patching" consequence in the spec with
the measured result.

```bash
git add -A
git commit -m "docs(spec) record camera collision behaviour"
```

---

## Task 6: Ownership, panic key and automatic release

Built before free-cam movement deliberately. The escape hatch exists before
there is anything to escape from.

**Files:**
- Create: `src/CinematicCam.Core/CameraOwnership.cs`
- Create: `tests/CinematicCam.Tests/CameraOwnershipTests.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Produces: `CinematicCam.Core.CameraOwnership` with `bool IsOwned`,
  `void Take()`, `void Release(string reason)`, `string? LastReleaseReason`.

- [ ] **Step 1: Write the failing tests**

`tests/CinematicCam.Tests/CameraOwnershipTests.cs`:

```csharp
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

public class CameraOwnershipTests
{
    [Fact]
    public void StartsUnowned()
    {
        Assert.False(new CameraOwnership().IsOwned);
    }

    [Fact]
    public void TakeMakesItOwned()
    {
        var ownership = new CameraOwnership();
        ownership.Take();
        Assert.True(ownership.IsOwned);
    }

    [Fact]
    public void ReleaseRecordsTheReason()
    {
        var ownership = new CameraOwnership();
        ownership.Take();
        ownership.Release("zone change");

        Assert.False(ownership.IsOwned);
        Assert.Equal("zone change", ownership.LastReleaseReason);
    }

    [Fact]
    public void ReleaseWhenAlreadyUnownedIsSafe()
    {
        var ownership = new CameraOwnership();
        ownership.Release("panic");
        ownership.Release("panic");
        Assert.False(ownership.IsOwned);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: build failure, `CameraOwnership` not found.

- [ ] **Step 3: Write the implementation**

`src/CinematicCam.Core/CameraOwnership.cs`:

```csharp
namespace CinematicCam.Core;

/// <summary>Whether the plugin currently owns the camera. Releasing is always safe.</summary>
public sealed class CameraOwnership
{
    public bool IsOwned { get; private set; }
    public string? LastReleaseReason { get; private set; }

    public void Take() => IsOwned = true;

    public void Release(string reason)
    {
        IsOwned = false;
        LastReleaseReason = reason;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: 6 passed.

- [ ] **Step 5: Wire the release paths**

In `Plugin.cs`, add:

```csharp
    internal static CameraOwnership Ownership { get; } = new();
```

Gate the state source:

```csharp
        Camera = new CameraController(() => Ownership.IsOwned ? TestState : null);
```

Make `hold` take ownership, and `release` go through the helper:

```csharp
            case "hold":
            {
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                TestState = current;
                Ownership.Take();
                Log.Information("[ccam] holding at {Pos}", current.Value.Position);
                break;
            }
            case "release":
                ReleaseCamera("command");
                break;
```

Add the helper and the event handlers:

```csharp
    private static void ReleaseCamera(string reason)
    {
        if (!Ownership.IsOwned && TestState is null) return;
        TestState = null;
        Ownership.Release(reason);
        Log.Information("[ccam] camera released: {Reason}", reason);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (Ownership.IsOwned && KeyState[VirtualKey.ESCAPE])
            ReleaseCamera("panic key");
    }

    private void OnTerritoryChanged(ushort territory)
        => ReleaseCamera($"zone change to {territory}");

    private void OnLogout(int type, int code)
        => ReleaseCamera("logout");
```

Subscribe in the constructor:

```csharp
        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        ClientState.Logout += OnLogout;
```

Unsubscribe first in `Dispose()`, before disposing the camera:

```csharp
        Framework.Update -= OnFrameworkUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Logout -= OnLogout;
        ReleaseCamera("plugin unload");
        Camera.Dispose();
```

Add `using Dalamud.Game.ClientState.Keys;`.

If `IClientState.Logout` has a different delegate signature in Dalamud 15, match
what the compiler reports. The behaviour matters, not the parameter shape.

- [ ] **Step 6: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 7: User runs the in-game checklist**

1. Click reload.
2. `/ccam hold`, confirm the view freezes, press **Escape**. Expected: camera
   returns to normal at once.
3. `/ccam hold`, then teleport to change zone. Expected: camera normal on
   arrival.
4. `/ccam hold`, then disable the plugin in `/xlplugins`. Expected: camera
   returns to normal, no crash.
5. Re-enable, `/ccam hold`, then log out to the title screen. Expected: no
   crash, camera normal on next login.

- [ ] **Step 8: Claude verifies**

```bash
grep -E "camera released|hook disposed" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -20
```

Expected: one `camera released` line per scenario with the matching reason —
`panic key`, `zone change to <id>`, `plugin unload`, `logout` — plus a
`hook disposed` line for scenario 4.

Escape is also the game's menu key. If holding the camera makes Escape unusable
for menus, that is acceptable for now; the binding becomes configurable with the
phase 3 hotkey work.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(camera) add ownership with panic key and auto-release"
```

---

## Task 7: Free-flying camera

The phase 1 deliverable.

**Files:**
- Create: `src/CinematicCam.Core/FreeCamMotion.cs`
- Create: `tests/CinematicCam.Tests/FreeCamMotionTests.cs`
- Create: `src/CinematicCam.Plugin/Game/FreeCam.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Produces: `CinematicCam.Core.FreeCamMotion` with
  `static Vector3 Step(Vector3 position, Vector3 input, float yaw, float pitch, float speed, float deltaSeconds)`
  where `input` is `(forward, up, right)` each in `[-1, 1]`, and
  `static Vector3 LookAtFrom(Vector3 position, float yaw, float pitch)`.
- Produces: `FreeCam` with `bool Enabled`, `void Enable(Vector3 startPosition)`,
  `void Disable()`, `CameraState? Tick(float deltaSeconds)`.

- [ ] **Step 1: Write the failing tests**

`tests/CinematicCam.Tests/FreeCamMotionTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

public class FreeCamMotionTests
{
    [Fact]
    public void NoInputDoesNotMove()
    {
        var start = new Vector3(10, 20, 30);
        Assert.Equal(start, FreeCamMotion.Step(start, Vector3.Zero, 0f, 0f, 5f, 0.016f));
    }

    [Fact]
    public void MotionIsFramerateIndependent()
    {
        var input = new Vector3(1, 0, 0);
        var oneBigStep = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 5f, 0.1f);

        var accumulated = Vector3.Zero;
        for (var i = 0; i < 10; i++)
            accumulated = FreeCamMotion.Step(accumulated, input, 0f, 0f, 5f, 0.01f);

        Assert.True(Vector3.Distance(oneBigStep, accumulated) < 0.0001f,
            $"expected {oneBigStep}, accumulated {accumulated}");
    }

    [Fact]
    public void SpeedScalesDistanceLinearly()
    {
        var input = new Vector3(1, 0, 0);
        var slow = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 1f, 1f);
        var fast = FreeCamMotion.Step(Vector3.Zero, input, 0f, 0f, 2f, 1f);

        Assert.True(fast.Length() > slow.Length() * 1.9f);
    }

    [Fact]
    public void UpInputMovesOnYOnly()
    {
        var result = FreeCamMotion.Step(Vector3.Zero, new Vector3(0, 1, 0), 1.2f, 0.4f, 3f, 1f);
        Assert.Equal(0f, result.X, 4);
        Assert.Equal(0f, result.Z, 4);
        Assert.True(result.Y > 0f);
    }

    [Fact]
    public void LookAtIsOneUnitAheadOfPosition()
    {
        var position = new Vector3(5, 5, 5);
        Assert.Equal(1f, Vector3.Distance(position, FreeCamMotion.LookAtFrom(position, 0f, 0f)), 4);
    }

    [Fact]
    public void PitchUpRaisesTheLookAtTarget()
    {
        var level = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0f);
        var raised = FreeCamMotion.LookAtFrom(Vector3.Zero, 0f, 0.5f);
        Assert.True(raised.Y > level.Y);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: build failure, `FreeCamMotion` not found.

- [ ] **Step 3: Write the implementation**

`src/CinematicCam.Core/FreeCamMotion.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core;

/// <summary>Free camera movement maths.</summary>
public static class FreeCamMotion
{
    /// <summary>Advances a camera position by one frame of input.</summary>
    /// <param name="input">(forward, up, right), each in [-1, 1].</param>
    /// <param name="yaw">Horizontal angle in radians.</param>
    /// <param name="pitch">Vertical angle in radians. Positive looks up.</param>
    /// <param name="speed">Units per second at full input.</param>
    public static Vector3 Step(Vector3 position, Vector3 input,
                               float yaw, float pitch, float speed, float deltaSeconds)
    {
        if (input == Vector3.Zero) return position;

        var forward = Direction(yaw, pitch);
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var move = (forward * input.X) + (Vector3.UnitY * input.Y) + (right * input.Z);

        return position + (move * speed * deltaSeconds);
    }

    /// <summary>A point one unit ahead of the camera along its facing.</summary>
    public static Vector3 LookAtFrom(Vector3 position, float yaw, float pitch)
        => position + Direction(yaw, pitch);

    private static Vector3 Direction(float yaw, float pitch)
    {
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(MathF.Sin(yaw) * cosPitch, MathF.Sin(pitch), MathF.Cos(yaw) * cosPitch);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: 12 passed.

If `UpInputMovesOnYOnly` fails, the `right` vector has a Y component. It must
stay horizontal so rising never drifts sideways.

- [ ] **Step 5: Write the plugin-side free cam**

`src/CinematicCam.Plugin/Game/FreeCam.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Game;

/// <summary>Flies the camera with the movement keys. Mouse-look still steers.</summary>
internal sealed class FreeCam
{
    private const float BaseSpeed = 8f;
    private const float SprintMultiplier = 4f;

    private Vector3 position;

    public bool Enabled { get; private set; }

    public void Enable(Vector3 startPosition)
    {
        position = startPosition;
        Enabled = true;
        Plugin.Log.Information("[freecam] enabled at {Pos}", position);
    }

    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        Plugin.Log.Information("[freecam] disabled");
    }

    public CameraState? Tick(float deltaSeconds)
    {
        if (!Enabled) return null;

        var (yaw, pitch) = ReadCameraAngles();
        var speed = BaseSpeed * (Plugin.KeyState[VirtualKey.SHIFT] ? SprintMultiplier : 1f);
        position = FreeCamMotion.Step(position, ReadInput(), yaw, pitch, speed, deltaSeconds);

        return new CameraState(
            position,
            FreeCamMotion.LookAtFrom(position, yaw, pitch),
            CameraAccess.ReadState()?.Fov ?? 0.78f);
    }

    private static Vector3 ReadInput()
    {
        var forward = 0f;
        var up = 0f;
        var right = 0f;

        if (Plugin.KeyState[VirtualKey.W]) forward += 1f;
        if (Plugin.KeyState[VirtualKey.S]) forward -= 1f;
        if (Plugin.KeyState[VirtualKey.D]) right += 1f;
        if (Plugin.KeyState[VirtualKey.A]) right -= 1f;
        if (Plugin.KeyState[VirtualKey.SPACE]) up += 1f;
        if (Plugin.KeyState[VirtualKey.CONTROL]) up -= 1f;

        return new Vector3(forward, up, right);
    }

    private static unsafe (float Yaw, float Pitch) ReadCameraAngles()
    {
        if (!CameraAccess.TryGetActiveCamera(out var camera)) return (0f, 0f);
        return (camera->DirH, camera->DirV);
    }
}
```

- [ ] **Step 6: Wire it into the plugin**

In `Plugin.cs`, add:

```csharp
    internal static FreeCam FreeCamera { get; } = new();
```

Change the controller's state source so free cam takes priority:

```csharp
        Camera = new CameraController(() =>
        {
            if (!Ownership.IsOwned) return null;
            return FreeCamera.Tick((float)Framework.UpdateDelta.TotalSeconds) ?? TestState;
        });
```

Add the verb:

```csharp
            case "fly":
            {
                if (FreeCamera.Enabled) { ReleaseCamera("fly toggled off"); break; }
                var current = CameraAccess.ReadState();
                if (current is null) { Log.Error("[ccam] cannot read camera state."); break; }
                FreeCamera.Enable(current.Value.Position);
                Ownership.Take();
                break;
            }
```

Extend `ReleaseCamera` so every escape path stops the free cam:

```csharp
    private static void ReleaseCamera(string reason)
    {
        if (!Ownership.IsOwned && TestState is null) return;
        FreeCamera.Disable();
        TestState = null;
        Ownership.Release(reason);
        Log.Information("[ccam] camera released: {Reason}", reason);
    }
```

- [ ] **Step 7: Build**

```bash
./build.sh
```

Expected: `Build succeeded.`

- [ ] **Step 8: User runs the in-game checklist**

1. Click reload.
2. Stand in an open outdoor area. Run `/ccam fly`.
3. Hold **W**. Expected: the camera flies forward along its facing. **The
   character will also walk** — input is not captured yet, and that is expected.
4. Try **A**, **S**, **D**, **Space** and **Ctrl**. Confirm each moves the camera
   as expected, with no sideways drift when rising.
5. Move the mouse to look around while holding **W**. Confirm flight direction
   follows the view.
6. Hold **Shift** with **W**. Confirm the camera moves noticeably faster.
7. Press **Escape**. Expected: free cam ends, camera returns to normal.
8. Report whether movement felt smooth or stuttery.

- [ ] **Step 9: Claude verifies**

```bash
grep -E "\[freecam\]|camera released" "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -20
```

Expected: `[freecam] enabled at <position>`, then `[freecam] disabled` paired
with `camera released: panic key`.

The character walking alongside the camera is the known gap. Input capture is
deliberately separate: it is the piece most likely to need iteration, and it
should not block the flying camera from being proven.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(camera) add free-flying camera"
```

---

## Phase 1 exit criteria

- `/ccam fly` flies the camera smoothly in all six directions.
- Escape returns the camera to normal from any state.
- Zone change, logout and plugin unload all release the camera.
- The field-of-view question has a recorded answer in the spec.
- The collision question has a recorded answer in the spec.
- `dotnet test` passes, 12 tests.

Deliberately left for follow-up, not phase 1 blockers:

- Suppressing character movement while flying.
- A configurable panic binding, which arrives with phase 3 hotkeys.
