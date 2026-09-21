# Phase 2 — tracks

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal, in your hands:** fly to three spots, press a key at each, set a duration, and watch
the camera glide through all three and hold on the last frame. The spec's phase 2 milestone
is "author a shot, play it back" (`spec:474-477`).

The UI is **not** in this phase. Authoring here happens through `/ccam` subcommands that
stand in for the editor. They are scaffolding for testing, not product surface — the real
authoring flow is a button and a hotkey in the track editor (`spec:333-335`).

## Part A — the Core layer

All in `CinematicCam.Core`: no Dalamud, no FFXIVClientStructs, no `unsafe`, no game. Every
test named is one the spec's own list already asks for (`spec:423-434`). I verify all of
this myself on macOS before you see any of it.

### A1 — track model
`ControlPoint`, `Track`, `SnapPoint`, `AimMode`, `Easing`, as `spec:167-171`. Per-point FoV
(`spec:174`). `SnapPoint` stays a distinct type so degenerate cases never reach the spline
(`spec:246-249`).

`feat(core) add the track model`

### A2 — centripetal Catmull-Rom
Alpha 0.5 (`spec:178`). The curve passes through every control point (`spec:180-183`).
Endpoints duplicate for phantom points; looping tracks wrap (`spec:188-190`). Two points is
a straight dolly (`spec:188-189`).

Tests: curve passes through its control points; zero, one, two and coincident points do not
throw (`spec:429`); the loop seam is continuous.

`feat(core) add centripetal catmull-rom evaluation`

### A3 — arc-length reparameterisation
Sample each segment, accumulate chord lengths, evaluate by distance not parameter
(`spec:196-198`). Table builds on edit, not per frame (`spec:199-201`).

Test: a bunched-then-spread track yields even spacing — the spec notes this test fails
without reparameterisation, which is its purpose (`spec:424-425`).

`feat(core) evaluate the spline by arc length`

### A4 — easing
`Linear`, `In`, `Out`, `InOut`, default smoothstep (`spec:227-228`). Ease normalised time,
then evaluate at that distance. Looping forces linear or the seam stutters (`spec:229`).

`feat(core) add easing curves`

### A5 — aim modes
LookAt, PathTangent (with fallback and pitch clamp, `spec:208-210`), AimKeys on separate
yaw and pitch channels, never slerped (`spec:213-215`). Yaw unwraps before interpolation
(`spec:217-219`).

Test: yaw crossing ±180° takes the short way.

`feat(core) add the three aim modes`

### A6 — playback
Duration-based (`spec:223-225`). Accumulate elapsed from a delta, not frame counts, so a
shot runs identically at 30 and 144fps (`spec:231-232`). A finished track holds its final
frame (`spec:251-253`).

Tests: position at t=5s identical under 60fps and 30fps delta sequences; a finished track
holds its last frame.

`feat(core) play a track over its duration`

### A7 — Director
`CameraState? Tick(float dt)` (`spec:237`). Non-null means we own the camera, null means
hands off (`spec:240-242`). Live mode off returns null (`spec:257-260`). A shot is a Track,
a SnapPoint, or GameCamera (`spec:246`).

Test: `Tick` returns null whenever live mode is off.

`feat(core) add the director`

## Part B — wiring it to the game

### B1 — feed the Director to the camera
`Plugin.cs` currently sources camera state from free-cam or the `/ccam hold` test state.
Add the Director: free-cam while authoring, Director while playing, never both.

Every existing release path — zone change, area transition, logout, unload — must stop
playback too, not just drop the camera.

`feat(camera) drive the camera from the director`

### B2 — authoring and playback commands
Scaffolding standing in for the editor:

- `/ccam track new` — start an empty track
- `/ccam track capture` — append the current camera as a control point (position, yaw,
  pitch, FoV), which is the fly-and-drop loop (`spec:329-331`)
- `/ccam track duration <seconds>`
- `/ccam track aim <lookat|tangent|keys>`, `/ccam track loop`
- `/ccam track play` / `/ccam track stop`
- `/ccam track info` — point count, duration, aim mode, logged

`feat(track) add authoring and playback commands`

## Your test pass

One in-game round, at the end:

1. `/ccam fly`, `/ccam track new`
2. Fly to a spot, `/ccam track capture`. Repeat twice more.
3. `/ccam track duration 20`, `/ccam track play`
4. The camera flies smoothly through all three points at even speed and **holds on the
   last frame** — it must not snap back to your character.
5. `/ccam track stop`, then `/ccam release` — normal camera and movement return.
6. Play again and change zone mid-shot — playback stops and the camera releases.

Step 4 is the one only you can judge: whether the motion looks smooth or lurches
(`spec:398-400`).

## Choices the spec does not make

Flagging rather than burying. Say if you would rather decide any of these.

- **Default aim mode for a captured track: AimKeys.** Capture records yaw and pitch
  (`spec:330`), so replaying what you were looking at is the natural default for a track
  built by flying. LookAt needs a target you cannot set without UI.
- **Pitch clamp for PathTangent: ±89°.** Spec asks for a clamp (`spec:209-210`) without a
  number.
- **Arc-length sampling: 60 samples per segment.** Implied by "a ten-point track is roughly
  600 samples" (`spec:200-201`).
- **A track with no points: `Tick` returns null.** Spec requires only that it not throw
  (`spec:429`); null follows from "null means hands off".
- **Default duration: 20 seconds**, so `track play` works before `track duration` is set.

## Out of scope

| | Why |
|---|---|
| The editor UI — three windows, scrub bar, 3D spline overlay, click-to-select, ImGuizmo | phase 2c (`spec:321-382`). The gizmo convention check is its first task, per `spec:380-382` |
| Switchboard: slots, program/preview, TAKE, hotkeys | phase 3 (`spec:479-481`) |
| Persistence — saving tracks between sessions | with the editor that produces them |
| Controller | unsupported |

## Risks

- **Part A is low risk.** No game, no interop; failures surface as failing tests.
- **Part B is where it gets real**, but it reuses the camera ownership already proven in
  phase 1. The new failure mode is a shot that looks wrong rather than a crash.
- **Distance stays unwritten.** Nothing in this phase touches `Camera.Distance`; the
  persistence hazard is unchanged.
