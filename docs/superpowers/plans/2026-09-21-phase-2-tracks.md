# Phase 2 — tracks

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal, in your hands:** fly to three spots, press a key at each, set the pacing, and watch
the camera glide through all three and hold on the last frame. The spec's phase 2 milestone
is "author a shot, play it back" (`spec:528-531`).

The UI is **not** in this phase. Authoring happens through `/ccam track` subcommands
standing in for the editor. They are scaffolding for testing, not product surface — the
real flow is a button and a hotkey (`spec:377-379`), and a curve editor (`spec:389-392`).

## Timing model

Revised twice on 2026-09-21 and the spec updated each time. Final model: **path and timing
are separate** (`spec:225-227`).

The control points and the spline through them describe geometry only. A separate **timing
curve** — normalised distance along the path, plotted against time — carries every pacing
decision (`spec:237-246`):

- steeper is faster, shallower is slower
- **a flat section is a hold**; dwelling needs no field on the point
- ease in and out are tangents at a key, not properties of a segment

`Auto` tangents make a smooth pass-through the **default** (`spec:248-252`). The camera
changes pace gradually through a point instead of switching abruptly at it, which is the
right default for a camera — an unexplained lurch reads as a bug.

This replaces the per-segment duration/easing model from earlier today. `ControlPoint` goes
back to pure geometry and `Hold` disappears as a field.

## Part A — the Core layer

All in `CinematicCam.Core`: no Dalamud, no FFXIVClientStructs, no `unsafe`, no game. Every
test named is one the spec's list asks for (`spec:472-486`). I verify all of part A myself
on macOS before you see any of it.

### A1 — track model
`ControlPoint`, `Track`, `TimingKey`, `TangentMode`, `SnapPoint`, `AimMode` as
`spec:169-171` and `spec:233-235`. Per-point FoV (`spec:174`). `SnapPoint` stays distinct so
degenerate cases never reach the spline (`spec:290-293`).

`feat(core) add the track model`

### A2 — centripetal Catmull-Rom
Alpha 0.5 (`spec:180`). The curve passes through every control point (`spec:182-184`).
Endpoints duplicate for phantom points; looping tracks wrap (`spec:190-192`). Two points is
a straight dolly (`spec:190-191`).

Tests: curve passes through its control points; zero, one, two and coincident points do not
throw (`spec:483`).

`feat(core) add centripetal catmull-rom evaluation`

### A3 — arc-length table
Sample each segment, accumulate chord lengths, evaluate by normalised distance rather than
by parameter (`spec:200-203`). Rebuilds on edit, not per frame.

Test: a bunched-then-spread track yields even spacing (`spec:473-474`).

`feat(core) evaluate the path by arc length`

### A4 — the timing curve
Monotone cubic Hermite through `TimingKey`s. `Auto` tangents from neighbours, limited so
the curve never decreases (`spec:254-257`) — a naive cubic overshoots and the camera
reverses. `Flat` pins tangents to zero. `Linear` and `Manual` are defined but unreachable
until the curve editor.

A hold falls out of the limiter rather than needing its own code path: two keys at the same
`Position` give a zero secant, the limiter zeroes both tangents, and the section is flat.
The same limiter is what eases a transition between legs of very different pace, because it
pulls the tangent at a junction toward the gentler of the two secants.

Ends hold their value (`spec:259-261`), which is where "a finished track holds its final
frame" comes from rather than a special case. Total length is the last key's time
(`spec:263`). Looping wraps modulo total with cyclic auto tangents at the seam
(`spec:265-269`).

Tests: a straight curve gives constant world speed on unevenly spaced points (`spec:477`);
a flat section holds the camera still (`spec:478`); the curve never decreases, including
for keys a naive cubic would overshoot (`spec:479-480`); the loop seam is continuous in
position and speed (`spec:482`).

`feat(core) add the timing curve`

### A5 — aim modes
LookAt, PathTangent (fallback and pitch clamp, `spec:210-212`), AimKeys on separate yaw and
pitch channels, never slerped (`spec:215-217`). Yaw unwraps before interpolation
(`spec:219-221`). Aim and FoV are read at the same point on the path as position, so they
sit still during a hold without special handling.

Test: yaw crossing ±180° takes the short way.

`feat(core) add the three aim modes`

### A6 — playback
`elapsed += dt` → evaluate the timing curve → look that normalised distance up on the
arc-length table → position, aim and FoV. Accumulates a delta rather than counting frames
so a shot runs identically at 30 and 144fps (`spec:275-276`).

Tests: position at t=5s identical under 60fps and 30fps delta sequences; a finished track
holds its last frame (`spec:295-297`).

`feat(core) play a track`

### A7 — key generation from legs and holds
The commands and, later, the editor need to write the curve without anyone typing tangents.
"This leg takes N seconds" and "hold here for N seconds" become keys with `Auto` and `Flat`
tangents. Appending a control point rescales existing keys so capturing a fourth point does
not retime the first three (`spec:271-273`).

Test: appending a control point does not retime the existing ones (`spec:481`).

`feat(core) build timing keys from legs and holds`

### A8 — Director
`CameraState? Tick(float dt)` (`spec:281`). Non-null means we own the camera, null means
hands off (`spec:284-286`). Live mode off returns null. A shot is a Track, a SnapPoint, or
GameCamera (`spec:290`).

Test: `Tick` returns null whenever live mode is off (`spec:486`).

`feat(core) add the director`

## Part B — wiring it to the game

### B1 — feed the Director to the camera
`Plugin.cs` sources camera state from free-cam or the `/ccam hold` test state. Add the
Director: free-cam while authoring, Director while playing, never both.

Every existing release path — zone change, area transition, logout, unload — must stop
playback, not just drop the camera.

`feat(camera) drive the camera from the director`

### B2 — authoring and playback commands
Scaffolding standing in for the editor. Nobody types a tangent; these write keys via A7.

- `/ccam track new`
- `/ccam track capture` — append the current camera as a control point (`spec:373-375`)
- `/ccam track leg <index> <seconds>` — how long the transition into that point takes
- `/ccam track hold <index> <seconds>` — a flat section at that point
- `/ccam track aim <lookat|tangent|keys>`, `/ccam track loop`
- `/ccam track play` / `/ccam track stop`
- `/ccam track info` — points, keys, total length

`feat(track) add authoring and playback commands`

## Your test pass

One in-game round, at the end:

1. `/ccam fly`, `/ccam track new`
2. Fly to a spot, `/ccam track capture`. Repeat twice more.
3. `/ccam track play` — the camera should cross all three points **smoothly**, with no stop
   or lurch at the middle one, and **hold on the last frame** rather than snapping back
4. `/ccam track leg 2 10` — make the second transition much slower, play again. It should
   ease into the slower pace, not switch abruptly
5. `/ccam track hold 1 3` — play again. Now it should stop dead on the middle point for
   three seconds
6. `/ccam track capture` a fourth point, `/ccam track info` — the first three legs keep
   their timings
7. `/ccam track stop`, `/ccam release` — normal camera and movement return
8. Play again and change zone mid-shot — playback stops and the camera releases

Steps 3 and 4 are the ones only you can judge: whether the motion looks smooth
(`spec:447-449`).

## Choices the spec does not make

Flagging rather than burying. Say if you would rather decide any of these.

- **Default leg duration: 5 seconds**, so `capture` produces a playable track immediately.
- **Default aim mode: AimKeys.** Capture records yaw and pitch (`spec:373-375`), so
  replaying what you looked at is the natural default for a track built by flying. LookAt
  needs a target you cannot set without UI.
- **Pitch clamp for PathTangent: ±89°** (`spec:211-212` asks for a clamp, no number).
- **Arc-length sampling: 60 samples per segment**, implied by "roughly 600 samples" for ten
  points (`spec:202-203`).
- **A track with no points: `Tick` returns null.** The spec requires only that it not throw
  (`spec:483`); null follows from "null means hands off".
- **Auto-tangent limiter: Fritsch–Carlson.** The spec requires monotonicity (`spec:254-257`)
  without naming a method. This is the standard one.

## Out of scope

| | Why |
|---|---|
| Curve editor — draggable keys and tangent handles | phase 2c (`spec:389-392`). Until then the curve is generated, not shaped |
| The rest of the editor UI — windows, scrub bar, 3D overlay, click-to-select, ImGuizmo | phase 2c (`spec:365-430`); the gizmo convention check is its first task (`spec:429-430`) |
| Switchboard: slots, program/preview, TAKE, hotkeys | phase 3 (`spec:533-535`) |
| Persistence — saving tracks between sessions | with the editor that produces them |
| Controller | unsupported |

## Risks

- **Part A is low risk.** No game, no interop; failures surface as failing tests. This is
  why the curve model is affordable — it is more machinery than per-segment timing, but all
  of it is verifiable without you.
- **The monotonicity limiter is the one subtle bit.** Get it wrong and the camera judders
  backwards at a key. It has a dedicated test (`spec:479-480`).
- **Part B reuses the camera ownership proven in phase 1.** The new failure mode is a shot
  that looks wrong rather than a crash.
- **Distance stays unwritten.** Nothing here touches `Camera.Distance`; the persistence
  hazard is unchanged.
