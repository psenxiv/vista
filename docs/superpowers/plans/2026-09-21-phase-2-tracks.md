# Phase 2 — tracks

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal, in your hands:** fly to three spots, press a key at each, give each leg its own
duration, and watch the camera glide through all three and hold on the last frame. The
spec's phase 2 milestone is "author a shot, play it back" (`spec:496-499`).

The UI is **not** in this phase. Authoring here happens through `/ccam track` subcommands
standing in for the editor. They are scaffolding for testing, not product surface — the
real flow is a button and a hotkey in the track editor (`spec:353-355`).

## Timing model

Revised on 2026-09-21 at your request; the spec was updated first (`spec:224-253`).

Each **leg** carries its own duration and easing. Each **point** may carry a hold. Playback
is: arrive at a point, hold for `Hold` seconds, travel the next leg over its
`DurationToNext` under its `EaseToNext`. Total shot length is the sum of all legs and
holds.

Arc-length reparameterisation moves from whole-track to **per leg** (`spec:233-236`): each
leg travels its own stretch of curve at even speed over its own duration. Nothing in part A
is wasted by the change.

Speed is deliberately discontinuous at interior points (`spec:238-243`). Adjacent legs of
different length and duration meet at a visible change of pace, and a leg easing out into a
leg easing in stops the camera dead at that point. Both are legitimate; neither is smoothed
automatically.

## Part A — the Core layer

All in `CinematicCam.Core`: no Dalamud, no FFXIVClientStructs, no `unsafe`, no game. Every
test named is one the spec's list asks for (`spec:443-456`). I verify all of part A myself
on macOS before you see any of it.

### A1 — track model
`ControlPoint`, `Track`, `SnapPoint`, `AimMode`, `Easing`, as `spec:169-172`. Per-point FoV
(`spec:175`). `SnapPoint` stays a distinct type so degenerate cases never reach the spline
(`spec:266-269`). `Track.TotalDuration` is computed, not stored.

`feat(core) add the track model`

### A2 — centripetal Catmull-Rom
Alpha 0.5 (`spec:179`). The curve passes through every control point (`spec:181-183`).
Endpoints duplicate for phantom points; looping tracks wrap (`spec:189-191`). Two points is
a straight dolly (`spec:189-190`).

Tests: curve passes through its control points; zero, one, two and coincident points do not
throw (`spec:451`); the loop seam is continuous.

`feat(core) add centripetal catmull-rom evaluation`

### A3 — per-leg arc-length reparameterisation
Sample each segment, accumulate chord lengths, evaluate by distance not parameter
(`spec:199-202`), scoped to one leg (`spec:233-236`). Tables build on edit, not per frame.

Test: a bunched-then-spread track yields even spacing within a leg (`spec:444-445`).

`feat(core) evaluate each leg by arc length`

### A4 — easing
`Linear`, `In`, `Out`, `InOut`, per leg (`spec:245-246`). Ease the leg's normalised time,
then evaluate at that distance along the leg.

`feat(core) add easing curves`

### A5 — aim modes
LookAt, PathTangent (fallback and pitch clamp, `spec:209-211`), AimKeys on separate yaw and
pitch channels, never slerped (`spec:214-216`). Yaw unwraps before interpolation
(`spec:218-220`). Aim follows the same eased local time as position, so the two stay in
sync; during a hold, aim and FoV sit still with the camera.

Test: yaw crossing ±180° takes the short way.

`feat(core) add the three aim modes`

### A6 — playback
Walk holds and legs in order. Accumulate elapsed from a delta, not frame counts, so a shot
runs identically at 30 and 144fps (`spec:251-252`). A finished track holds its final frame
(`spec:271-273`).

Tests: position at t=5s identical under 60fps and 30fps delta sequences; each leg takes
exactly its own duration independent of its length (`spec:448`); a hold keeps the camera
still for its duration then the next leg starts (`spec:449`); a finished track holds its
last frame.

`feat(core) play a track leg by leg`

### A7 — Director
`CameraState? Tick(float dt)` (`spec:257`). Non-null means we own the camera, null means
hands off (`spec:260-262`). Live mode off returns null. A shot is a Track, a SnapPoint, or
GameCamera (`spec:266`).

Test: `Tick` returns null whenever live mode is off (`spec:454`).

`feat(core) add the director`

## Part B — wiring it to the game

### B1 — feed the Director to the camera
`Plugin.cs` sources camera state from free-cam or the `/ccam hold` test state. Add the
Director: free-cam while authoring, Director while playing, never both.

Every existing release path — zone change, area transition, logout, unload — must stop
playback, not just drop the camera.

`feat(camera) drive the camera from the director`

### B2 — authoring and playback commands
Scaffolding standing in for the editor:

- `/ccam track new`
- `/ccam track capture [leg] [hold]` — append the current camera as a point
  (`spec:349-351`); `leg` is the duration of the transition *into* this point, `hold` the
  pause on arrival
- `/ccam track leg <index> <seconds> [linear|in|out|inout]`
- `/ccam track hold <index> <seconds>`
- `/ccam track aim <lookat|tangent|keys>`, `/ccam track loop`
- `/ccam track play` / `/ccam track stop`
- `/ccam track info` — every point with its hold, its outgoing leg and ease, and the total

`feat(track) add authoring and playback commands`

## Your test pass

One in-game round, at the end:

1. `/ccam fly`, `/ccam track new`
2. Fly to a spot, `/ccam track capture`. Repeat twice more.
3. `/ccam track leg 1 2` and `/ccam track leg 2 10` — make the second leg five times slower
4. `/ccam track hold 1 3` — a three second beat on the middle point
5. `/ccam track play`
6. The camera crosses the first leg briskly, **stops dead on the middle point for three
   seconds**, crawls the second leg, and **holds on the last frame** — it must not snap
   back to your character
7. `/ccam track stop`, `/ccam release` — normal camera and movement return
8. Play again and change zone mid-shot — playback stops and the camera releases

Step 6 is the one only you can judge: whether the motion looks smooth or lurches
(`spec:418-420`).

## Choices the spec does not make

Flagging rather than burying. Say if you would rather decide any of these.

- **Default easing per leg: `Linear`.** This one matters. The spec's old whole-track
  default was smoothstep, but per leg that would ease out and back in at *every* point,
  stopping the camera dead at each one. Linear keeps a captured track moving; you add ease
  where you want it.
- **Default leg duration: 5 seconds. Default hold: 0.**
- **Default aim mode: AimKeys.** Capture records yaw and pitch (`spec:349-351`), so
  replaying what you looked at is the natural default for a track built by flying. LookAt
  needs a target you cannot set without UI.
- **Pitch clamp for PathTangent: ±89°** (`spec:210-211` asks for a clamp, no number).
- **Arc-length sampling: 60 samples per leg**, implied by "roughly 600 samples" for ten
  points (`spec:201-202`).
- **A track with no points: `Tick` returns null.** The spec requires only that it not throw
  (`spec:451`); null follows from "null means hands off".

## Out of scope

| | Why |
|---|---|
| The editor UI — three windows, scrub bar, 3D spline overlay, click-to-select, ImGuizmo | phase 2c (`spec:341-401`). The gizmo convention check is its first task (`spec:400-401`) |
| Switchboard: slots, program/preview, TAKE, hotkeys | phase 3 (`spec:501-503`) |
| Continuous velocity across a whole track | needs tangent handles on a velocity curve (`spec:238-243`) |
| Persistence — saving tracks between sessions | with the editor that produces them |
| Controller | unsupported |

## Risks

- **Part A is low risk.** No game, no interop; failures surface as failing tests.
- **Part B reuses the camera ownership proven in phase 1.** The new failure mode is a shot
  that looks wrong rather than a crash.
- **Distance stays unwritten.** Nothing here touches `Camera.Distance`; the persistence
  hazard is unchanged.
