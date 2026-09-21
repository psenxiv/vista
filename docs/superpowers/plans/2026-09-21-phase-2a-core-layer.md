# Phase 2a — the Core layer

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal:** given a list of control points and a duration, produce a `CameraState` for any
moment in the shot. Entirely inside `CinematicCam.Core` — no Dalamud, no
FFXIVClientStructs, no `unsafe`, no game.

## Why this is split from the rest of phase 2

The spec's phase 2 (`spec:474-478`) is "the whole Core layer under TDD, **then** the camera
wiring, the gizmo convention check, and the editor". This plan is only the first clause.
Wiring and the editor get their own plans once this is built, per your instruction to plan
each step after the prior one lands.

**Batch size does not apply here the way it did in 1b.** That rule was about changes only
you can verify; bundling six of them let a bad one ride in. Everything below is verified by
unit tests on my side before you see it, so the whole layer can land in one go. The first
thing needing your eyes is playback in game, which is phase 2b.

## Tasks

Each task lands with its tests. Every test named below is one the spec already asks for
(`spec:423-434`).

### Task 1 — track model

`ControlPoint`, `Track`, `SnapPoint`, `AimMode`, `Easing`. Records, exactly as
`spec:167-171`. Per-point FoV included (`spec:174`).

`SnapPoint` stays a distinct type rather than a one-point track, so degenerate cases never
reach the spline (`spec:246-249`).

`feat(core) add the track model`

### Task 2 — centripetal Catmull-Rom

Alpha 0.5 (`spec:178`). The curve passes through every control point (`spec:180-183`).
Endpoints duplicate to supply phantom points; looping tracks wrap instead (`spec:189-190`).
A two-point track degenerates to a straight dolly (`spec:188-189`).

Tests: curve passes through its control points; degenerate input — zero, one, two and
coincident points — does not throw; the loop seam is continuous.

`feat(core) add centripetal catmull-rom evaluation`

### Task 3 — arc-length reparameterisation

Sample each segment densely, accumulate chord lengths, evaluate by distance rather than by
parameter (`spec:196-198`). The table builds on edit, not per frame (`spec:199-201`).

Test: a deliberately bunched-then-spread track yields even spacing. The spec notes this
test fails without reparameterisation, which is its purpose (`spec:424-425`).

`feat(core) evaluate the spline by arc length`

### Task 4 — easing

`Linear`, `In`, `Out`, `InOut`, default smoothstep (`spec:227-228`). Ease normalised time,
then evaluate at that distance. Looping tracks force linear or the seam stutters
(`spec:229`).

`feat(core) add easing curves`

### Task 5 — aim modes

Three, per track (`spec:205-219`):
- **LookAt** — `normalize(target - position)`
- **PathTangent** — analytic spline derivative, falling back to the last valid direction
  when coincident points collapse it, with a pitch clamp against gimbal (`spec:208-210`)
- **AimKeys** — yaw and pitch splined on separate channels, never slerped, so the horizon
  stays level (`spec:213-215`)

Yaw unwraps before interpolation: walk the keys adding or subtracting 2π so no two
consecutive values differ by more than π (`spec:217-219`).

Test: yaw crossing ±180° takes the short way.

`feat(core) add the three aim modes`

### Task 6 — track playback

Duration-based (`spec:223-225`). Accumulate elapsed time from a delta rather than counting
frames, so a shot runs identically at 30 and 144 fps (`spec:231-232`). A finished track
holds its final frame and does not revert to the game camera (`spec:251-253`).

Tests: position at t=5s is identical under 60fps and 30fps delta sequences; a finished
track holds its last frame.

`feat(core) play a track over its duration`

### Task 7 — Director

`CameraState? Tick(float dt)` (`spec:237`). Non-null means the plugin owns the camera; null
means hands off. Live mode off returns null (`spec:257-260`). A shot is a Track, a
SnapPoint, or GameCamera (`spec:246`).

Test: `Tick` returns null whenever live mode is off.

`feat(core) add the director`

## Choices the spec does not make

Flagging these rather than burying them. All are implementation constants with no
user-visible policy behind them; say if you would rather decide any of them.

- **Pitch clamp for PathTangent**: ±89°. The spec asks for "a pitch clamp so a
  near-vertical path does not gimbal" (`spec:209-210`) without giving a number.
- **Arc-length sample density**: 60 samples per segment. The spec says a ten-point track is
  "roughly 600 samples" (`spec:200-201`), which implies this.
- **A track with no points**: `Tick` returns null. The spec requires only that it not throw
  (`spec:429`); null follows from "null means hands off" (`spec:240-242`).

## Out of scope

| | Why |
|---|---|
| Switchboard slots, program/preview, TAKE, flip-flop | phase 3 (`spec:479-481`); the spec lists TAKE tests but they belong there |
| Camera wiring — feeding Director output to `CameraController` | phase 2b |
| Editor UI, ImGuizmo, the 3D overlay | phase 2c, after the gizmo convention check |
| Persistence and config round-tripping | phase 2c or 3, with the editor that produces the data |
| Controller support | unsupported |

## Risks

- **Low.** No game, no interop, no unverifiable claims. The failure mode is a shot that
  looks wrong rather than a crash or a corrupted save, and it surfaces in phase 2b.
- The one thing tests cannot catch is whether a curve *looks* right in motion. That is
  yours to judge in 2b, and the spec says as much (`spec:398-400`).
