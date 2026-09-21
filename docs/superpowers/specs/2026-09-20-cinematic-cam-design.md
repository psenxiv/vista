# Cinematic Cam — Design

Date: 2026-09-20
Status: Approved, ready for implementation planning

## Purpose

A Dalamud plugin that gives FFXIV content creators real camera work: paths the
camera flies along with controlled aim, static camera positions it can jump to,
and a switchboard for cutting between them live.

Existing camera mods offer static position-and-point cameras with no motion and
no organised way to switch between shots. The workaround is running alt accounts
as extra "cameras" and switching between their windows in OBS. That burns a
machine per camera and does not scale past a few angles.

Target users: event runners, performance venues and streamers who want a fly
camera panning over an audience, or a planned sequence of shots cut live.

## Scope

In scope for v1:

- Camera tracks: a path through control points with two aim modes.
- Snap points: static camera positions the switchboard can cut to.
- Live mode: a program/preview switchboard with hard cuts and hotkeys.
- An editor with a 3D path overlay, click-to-select and gizmo editing.
- Persistence in the Dalamud plugin config.

Not in scope for v1. Each entry in `FEATURES.md` records why:

- Export/import of shows, and clipboard sharing.
- Eased-move and fade-through-black transitions.
- Aim tracking a game entity.
- Aim locked onto a fixed point (LookAt).
- Continuous flight recording.
- Playlists and auto-advance.
- Detecting or mitigating conflicts with Cammy. Treated as user error.

## Architecture

Three projects. The split is enforced by the compiler, not by discipline:

| Project | Target | Contents |
|---|---|---|
| `CinematicCam.Core` | `net10.0` | Spline, timing, Director, Switchboard, DTOs. No Dalamud reference, no `unsafe`. |
| `CinematicCam.Plugin` | `net10.0-windows` | Dalamud entry point, camera hook, ImGui, hotkeys. |
| `CinematicCam.Tests` | `net10.0` | References `Core` only. |

`Core` cannot reference Dalamud because its project file does not allow it.
Game types therefore cannot leak into the layer under test.

The layers:

```
CameraController   writes game memory. the only dirty code.
Director           shot + elapsed time -> CameraState
Track / Spline     geometry, interpolation, timing
Switchboard        which shot is live, which is staged, what TAKE does
```

`CameraState { Vector3 Position; Vector3 LookAt; float Fov }` is the seam
between them.

## Camera ownership

Hook `CameraBase.Update()` — virtual function 3, named in FFXIVClientStructs —
on the world camera (`CameraManager->Camera`, slot 0), never `GetActiveCamera()`:
another `Camera`-derived class overrides `Update`. Call the original, let the
game finish its own camera update, then overwrite `SceneCamera.Position` (offset
`0x50` on `Graphics.Scene.Object`) and `SceneCamera.LookAtVector` (offset `0x80`
on `Graphics.Scene.Camera`).

One hook, one write site. The plugin does not reimplement any game camera logic.

Two consequences follow from writing after `Update()` returns:

1. **Cammy loses by construction.** Cammy detours run inside `Update()`; ours
   writes after it. We are last, regardless of plugin load order.
2. **Camera collision needs no patching.** Confirmed in-game 2026-09-21: the
   camera passes straight through walls and terrain with no clamping or
   push-back. Cammy patches the geometry collision check in assembly to achieve
   this; we get it free, because the correction happens inside `Update()` and we
   overwrite its result. No signature scan, so nothing here breaks on a game
   patch.

### Roles and input

The plugin is in editing mode, live mode, or off. In editing mode the user directs,
flying the free-cam to build tracks; in live mode they operate the camera and run
the switchboard, nothing else. In both, the character is locked in place, and
movement keys and zoom are blocked. Chat stays usable.

### Resolved: what we write

Measured in-game 2026-09-21. Four fields, written after `Update()` returns, fully
own the camera. Nothing the game does afterwards overrides them:

| Field | Location | Why |
|---|---|---|
| Position | `SceneCamera.Object.Position`, `0x50` | where the camera is |
| Look-at point | `SceneCamera.LookAtVector`, `0x80` | what it points at |
| Up vector | `SceneCamera.Vector_1`, `0x90` | stops roll |
| Field of view | `Camera.FoV`, `0x130`, radians | zoom |

`Camera.Distance` and `InterpDistance` are **not** written; see the hazard below.
Zoom input makes the game interpolate its distance against our imposed position,
which shows as jitter that snaps back when scrolling stops. Blocking zoom input
while we own the camera removes that.

### Hazard: writing fields the game persists

Confirmed the hard way on 2026-09-21. Writing `Distance` every frame corrupted a
character's saved camera settings. The camera sat just above the character's
head, survived a relog, survived a full client restart, and survived disabling
Dalamud entirely. It affected only the character that had been used for testing.
The fix was Character Configuration -> Control Settings -> Return to Default.

Position, look-at and the up vector are recomputed every frame and are
self-correcting, so writing them is safe. `Distance` is a *setting*, saved into
the character's `COMMON.DAT`.

**A crash is not required.** The game writes character settings on logout, on
zoning and when the settings UI is used. Any of those firing while the plugin
holds the camera persists our values. Restore-on-release does not help, because
the save happens during the takeover rather than after it.

Two rules follow, and they apply to any field added to the write set later:

1. Before writing a game field every frame, establish whether it is runtime
   state or a saved setting. If it is saved, do not write it.
2. The reason `Distance` was pinned was to stop the game interpolating against
   our imposed position, and that only ever happened in response to zoom input.
   Blocking the input removes the need to write the field. Prefer suppressing
   the input over overwriting the setting.

`/ccam reset` restores stock values in memory as a user-facing escape hatch, but
it cannot undo what has already been written to disk.

The up vector is required, not optional. Leaving it to the game produces a
visible roll, because the game keeps deriving it from the direction it *intends*
to look while we have overridden the direction it actually looks. Its formula,
confirmed to five decimal places against captured values, is world up projected
onto the plane perpendicular to the view direction, left un-normalised:

```
up = worldUp - forward * dot(worldUp, forward)
```

`CameraOrientation.UpFor` implements this in `Core`, with a regression test
built from the captured values.

`TiltOffset` is not involved; it read zero throughout.

Writing these four is race-independent: all are world-space and none reference
the character model. Race affects the game's own `lookAtHeightOffset`, which
stops applying once we own the camera. Any future feature that aims at a person
rather than a point will need to account for per-race height.

## Track model

```csharp
record ControlPoint(Vector3 Position, float Yaw, float Pitch, float Fov);
record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<TimingKey> Timing,
             AimMode Aim, PlaybackMode Playback);
enum PlaybackMode { Once, Loop }
```

Per-point FoV costs one float and enables push-ins during a move.
`TimingKey` is defined under Timing below; the points describe the path, the
timing curve describes the pacing along it.

### Spline

Centripetal Catmull-Rom, alpha 0.5.

Catmull-Rom interpolates: the curve passes through every point the user flew to
and dropped. A B-spline would smooth past them, which would confuse someone who
just positioned a shot by eye.

The centripetal parameterisation avoids the cusps and self-intersections that
uniform Catmull-Rom produces on unevenly spaced points. A human dropping
waypoints by hand always produces uneven spacing.

Endpoints duplicate to supply the phantom points. A two-point track degenerates
to a straight dolly, which is a legitimate shot. The path is always open: its
ends are never joined, whatever the playback mode.

### Arc-length reparameterisation

Catmull-Rom's parameter is not proportional to distance. Animating the parameter
linearly makes the camera crawl through closely spaced points and lurch across
widely spaced ones. On a slow pan over an audience this ruins the shot.

On edit, sample each segment densely, accumulate chord lengths into a cumulative
table, and evaluate by distance along the curve rather than by parameter. The
table rebuilds on edit, not per frame: a ten-point track is roughly 600 samples,
computed once.

### Aim

`AimMode` is per track.

- **PathTangent** — the analytic spline derivative, with a pitch clamp so a
  near-vertical path does not gimbal. Where coincident points collapse the
  derivative, it falls back to the nearest valid direction along the path. The
  fallback depends only on the place on the path, never on previous frames, so
  scrubbing and playback agree.
- **AimKeys** — yaw and pitch per point, splined on separate channels.

AimKeys does not slerp quaternions. Slerp between two look directions can
introduce roll, and a camera that rolls unintentionally reads as broken.
Separate yaw and pitch channels keep the horizon level.

Yaw unwraps before interpolation: walk the key sequence adding or subtracting
2π so no two consecutive values differ by more than π. Without this, a shot
crossing due north whips the long way around.

Yaw, pitch and FoV are each splined between points by the fraction of the
segment's arc length travelled, the same place on the path as position. They
therefore sit still during a hold with no special handling.

### Timing

A track separates **where** from **when**. The control points and the spline
through them describe the path and nothing else. A separate **timing curve** says
how far along that path the camera is at each moment. This is how Unreal's
Sequencer and Unity's Cinemachine both work, and the separation is what makes
pacing editable without touching the shot's geometry.

```csharp
enum TangentMode { Auto, Linear, Flat, Manual }
record TimingKey(float Time, float Position, TangentMode Mode,
                 float InTangent, float OutTangent);
```

`Position` is a place on the path in control-point units: the whole part is the
segment, the fraction is how far along that segment's arc length. 0 is the first
control point, 2 the third, 2.5 halfway along the segment from the third to the
fourth. Playback evaluates the curve over distance along the path, so speed stays
continuous through a point and a straight line between keys is constant speed.

Keys are anchored to control points so editing geometry never retimes a shot:
moving or appending a point leaves every key in place. Inserting or deleting a
point renumbers the keys after it.

Every pacing decision is a shape in this curve:

- A steeper section is faster, a shallower section slower.
- **A flat section is a hold.** The camera stays still for its width. Dwelling on
  a point is not a separate feature and needs no field on the point.
- Ease in and ease out are the tangents at a key, not a property of a segment.

`Auto` tangents are computed from the neighbouring keys, which makes a smooth
pass-through the **default**: the camera changes pace gradually through a point
rather than switching abruptly at it. `Flat` pins both tangents to zero and brings
the camera to a stop at that key. `Linear` and `Manual` exist for the curve editor
and are not reachable before it ships.

`Auto` tangents at the first and last keys are one-sided: a track starts at full
speed and stops dead at its end. Easing in or out is the user's, via a `Flat` key.

**Position must never decrease.** A naive cubic through keys overshoots, which
would make the camera reverse briefly — visible as a judder and easily mistaken
for a bug. Auto tangents are limited so the curve stays monotone, and manual
tangents are clamped on evaluation.

Before the first key and after the last, the curve holds its end value. The rule
that a finished track holds its final frame therefore falls out of evaluation
rather than being a special case.

Total shot length is the time of the last key.

**Playback mode.** How elapsed time maps onto the timing curve, per track. `Once`
plays to the last key and holds. `Loop` cuts straight back to the first key when
it reaches the last and plays again: a hard cut, not a transition. A seamless loop
is the user's to build, by placing the last point and key to match the first.
Future modes are new values here; they change only how elapsed time is mapped,
never the path or the timing curve.

Playback accumulates `IFramework.UpdateDelta` rather than counting frames, so a
shot runs identically at 30 and 144 fps.

## Director

```csharp
CameraState? Tick(float dt);
```

The nullable return is the entire control protocol. Non-null means the plugin
owns the camera and `CameraController` writes it. Null means
hands off. "Live mode is off", "this slot is the game camera" and "control
released" all collapse into that one rule, so exactly one place decides whether
the game keeps its camera.

A shot is a Track, a SnapPoint, or GameCamera. A SnapPoint holds position, yaw,
pitch and FoV, captured from the free-cam with one key. It stays a distinct type
rather than a one-point track, which keeps degenerate cases out of the spline
code.

**A finished `Once` track holds its final frame.** It does not revert to the game
camera. Snapping back to the player's head mid-broadcast would be a disaster on
stream. The camera freezes where the track ended and the UI reports it.

### Live mode

Live mode is the master toggle. On, the plugin owns the camera and the
switchboard is active. Off, the editor still works and the game camera is
untouched. It is the boundary between building shots and running them, and the
safety switch: turning it off hands the camera back. There is one live mode;
playing a single track is live mode with that track on program.
Nothing can be edited while live, paused included; leave live mode to edit.

## Switchboard

Slots hold shots. One slot is program (live), one is preview (staged). TAKE
makes the staged shot live and restarts it from zero.

TAKE flip-flops: the outgoing program shot moves into the preview slot, as a
broadcast switcher does. Repeated presses then bounce between two shots — stage,
crowd, stage, crowd — which is the common case at an event. This is a one-line
behaviour and ships as a toggle.

**Preview is armed, not visible.** There is one camera, so the staged shot
cannot be shown without going to it. The UI displays the staged shot's name,
start position and distance from the live camera. The naming stays honest about
this rather than implying a monitor that cannot exist.

### Hotkeys

Bind TAKE, preview next and previous, and direct slot selection through
`IKeyState`. An operator cannot hunt for buttons during a show.

Hotkeys suppress while a text field holds focus. Cutting to camera 3 because
someone typed "3" in party chat is the kind of failure that gets a plugin
uninstalled.

### Safety

**Automatic release** on area transition, logout and plugin unload. Track
coordinates belong to one zone, so holding a stale camera through a loading
screen is meaningless and alarming. `Dispose` unhooks and restores; a plugin
crash unloads the hook and reverts the camera on its own.

The trigger is the `BetweenAreas` condition flag rather than `TerritoryChanged`,
which only fires when the territory id actually changes. The flag also catches
an aethernet hop inside one zone, a cutscene starting and a duty beginning.

**No dedicated panic key.** Escape was tried and rejected: too much game UI
depends on it, it is easy to hit by accident, and this plugin's own windows may
want it for closing dialogs. Cammy has no keyboard panic key either, though it
does bind free-cam exit to a repurposed game input.

Input capture blocks only movement keys and zoom, so chat stays reachable and
`/ccam release` remains an escape route alongside the automatic paths.

## Cammy coexistence — out of scope

No detection, no warning, no mitigation work. Running Cammy's free-cam or firing
a Cammy preset while this plugin holds the camera is user error, and v1 treats
it as such.

The one thing that remains true is free: because this plugin writes after
`CameraBase.Update()` returns and Cammy's detours run inside it, this plugin
wins any contest over camera position regardless of load order. That is a
consequence of the architecture, not work to be done.

Cammy 2.1.1.2 is installed on the development machine, which makes it a
convenient way to confirm that property holds in practice.

## Editor UI

Three windows: a library of tracks and snap points filtered to the current zone,
a track editor, and a compact switchboard intended to stay on screen during a
show.

### Authoring flow

The primary loop is fly and drop. Enter free-cam, fly to a position, then
capture the camera as a control point: position, yaw, pitch and FoV, appended to
the end of the track. Repeat. A track is built by flying it.

Capture is available two ways, and both exist in v1: a **Capture current camera**
button in the track editor, and a hotkey for the same action so the operator
does not have to reach for the mouse mid-flight.

Editing is a separate activity and uses the tools below: select a point, then
adjust it with a gizmo, re-capture it from the current camera, or type exact
numbers. Points can be inserted, reordered and deleted from the list.

The track editor carries a **scrub bar** — drag to see the camera at any moment
in the shot without playing it. This falls out free, because the Director is a
pure function of elapsed time.

It also carries a **curve editor** for the timing curve: position along the path
plotted against time, with draggable keys and tangent handles. Until it exists the
curve is generated from simple "this leg takes N seconds" and "hold here for N
seconds" inputs, which is enough to author a shot but not to shape one.

**3D overlay.** In v1, drawn on the ImGui foreground draw list over the game,
projected with `Camera.WorldToScreen`:

- **The spline itself**, densely sampled and drawn as a continuous polyline, so
  the actual flight path is visible in world space rather than inferred from the
  points.
- **A marker per control point**, numbered in track order.
- **An aim indicator per control point** — a short line from the point along its
  look direction, so position and direction are both readable at a glance.

The overlay is what makes the track editable by eye. It is not a nice-to-have
layered on afterwards: click-to-select and gizmo editing both depend on the same
projection, so it is built first among the editor work.

**Click-to-select.** Control points already project to screen for the overlay,
so hit-testing a click against those markers costs almost nothing.

**Gizmo editing** via `Dalamud.Bindings.ImGuizmo`, which ships in the Dalamud
dev assemblies:

- Translate on the selected control point.
- Rotate for aim direction, in AimKeys mode only.
- No scale. It means nothing here and the gizmo never offers it.

Re-capture-from-current-camera remains alongside the gizmo. It is the fastest
way to set a point while standing in the shot. Numeric fields stay underneath as
the precise fallback.

**Gizmo risk.** ImGuizmo needs view and projection matrices in the correct
convention. Wrong handedness or row-versus-column-major yields a gizmo that
looks correct but drags along the wrong axis. `SceneCamera.ViewMatrix` and
`RenderCamera->ProjectionMatrix` supply the inputs; matching FFXIV's convention
is the work. This is the **first** task of phase 2c, verified against a
known control point. Left until last, it becomes the thing dropped when the
phase overruns.

## Storage

The Dalamud plugin config, as JSON, carrying a `Version` field from the first
commit so migrations stay possible.

A `Show` groups tracks, snap points and a switchboard layout, and records its
`TerritoryType` so the UI filters to the current zone and warns on a mismatch.

The stored types are the same pure records from the track model, which makes
round-trip a unit test. That test gets written early: `Vector3` serialisation
has a history of surprising people.

## Testing

In-game verification belongs to the user. The development machine runs macOS and
the game runs under Wine; Claude cannot see the game, drive it, or judge whether
a shot looks right.

Claude can read `~/Library/Application Support/XIV on Mac/logs/dalamud.log`
while the game runs. It is plain text, timestamped and tagged per plugin. Crash
dumps land in the same directory. A failure therefore reaches Claude as a stack
trace rather than as a paraphrase.

Four measures keep the user's loop short:

1. **Verbose logging** of every hook install, camera write and state transition,
   behind a debug toggle.
2. **`/ccam selftest`** — the plugin asserts what Claude cannot observe and
   writes results to the log: camera pointer non-null, hook installed, and FoV
   written then read back with the delta logged. This answers the phase 1 FoV
   question from a single launch and one command.
3. **Scripted checklists** per phase: numbered steps with exact expected
   results, a few minutes each. Not "does this feel right".
4. **Hot reload.** Dalamud reloads a dev plugin without restarting the game.
   Phase 0 confirms this works under Wine; it decides whether iteration costs
   ten seconds or three minutes.

Tests that run on macOS with no game, covering where the real bugs live:

- The curve passes through its control points.
- A deliberately bunched-then-spread track yields even spacing. This test fails
  without arc-length reparameterisation, which is its purpose.
- Yaw crossing ±180° takes the short way.
- Position at t=5s matches under 60fps and 30fps delta sequences.
- A straight timing curve gives constant world speed on unevenly spaced points,
  and speed stays continuous through a key between unequal segments.
- A flat section of the timing curve holds the camera still for its width.
- The timing curve never decreases, including for keys a naive cubic would
  overshoot.
- Appending a control point does not retime the existing ones.
- A `Loop` track cuts back to its first frame after its last key.
- Degenerate input — zero, one, two and coincident points — does not throw.
- TAKE resets elapsed time; flip-flop swaps the slots.
- A finished `Once` track holds its last frame.
- `Tick` returns null whenever live mode is off.
- Zone change releases control.
- Config round-trips, `Vector3` included.

## Build environment

XIV on Mac is installed with Dalamud 15.0.3.5. Dev reference assemblies sit at
`~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/`, which is what
`Dalamud.NET.Sdk` builds against. On Windows it reads
`%AppData%\XIVLauncher\addon\Hooks\dev`, so `DALAMUD_HOME` points at the Mac
path instead.

Dalamud 15.0.3.5 targets **net10.0**. Its `runtimeconfig.json`
declares `"tfm": "net10.0"` and requires `Microsoft.NETCore.App 10.0.0`. All
three projects therefore target .NET 10, which the machine's SDK 10.0.301 and
runtime 10.0.9 satisfy natively.

`Dalamud.NET.Sdk/15.0.0` builds on macOS with no modification. The plugin sets
`<AssemblyName>CinematicCam</AssemblyName>` so DalamudPackager locates the
manifest, and build output lands flat in `bin/Debug/` rather than in a
framework-named subdirectory.

Verified during phase 0.

## Phasing

**Phase 0 — prove the toolchain.** A hello-world plugin building under
`Dalamud.NET.Sdk/15.0.0` with SDK 10 cross-targeting `net10.0-windows`,
`DALAMUD_HOME` pointed at the XIV on Mac path, dev mode enabled, the Wine path
mapping resolved, and the plugin loading and writing to the log. Hot reload
confirmed. Nothing else starts until this passes. A build-chain failure must
surface on day one.

**Phase 1 — own the camera.** Hook `Update()`, write position and look-at, run
the FoV and `ViewMatrix` spike through `/ccam selftest`, build the authoring
free-cam with its input capture and movement lock, and implement automatic
release on zone change, area transition, logout and unload. Ends with: a camera
that flies, and always releases. Little code, most of the project's risk.

**Phase 2 — tracks.** The whole `Core` layer under TDD on macOS: spline,
arc-length table, two aim modes, timing curve, Director. Then the camera wiring,
with a simple test window standing in for the editor. Ends with: author a shot,
play it back. Most of the code, least of the risk.

**Phase 2c — the editor.** The gizmo convention check first, then the 3D
overlay, click-to-select, gizmo editing, the scrub bar and the curve editor.

**Phase 3 — the switchboard.** Slots, program and preview, TAKE, hotkeys with
the text-focus guard, snap points on the bus, persistence. Ends with: run a show.

Phase 1 is small and dangerous; phase 2 is large and safe. Expect phase 1 to
feel slow for how little it visibly produces.

## Attribution

Cammy (https://github.com/UnknownX7/Cammy/) ships no license file. It was read
as reference for which game functions matter and what problems arise. No code is
copied. The camera approach here differs: one hook on `CameraBase.Update()` with
a post-pass, against current FFXIVClientStructs, rather than detours on five
vtable entries that reimplement game logic.
