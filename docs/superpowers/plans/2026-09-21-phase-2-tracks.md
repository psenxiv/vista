# Phase 2 — tracks

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal, in your hands:** fly to three spots, press a key at each, set the pacing, and watch
the camera glide through all three and hold on the last frame. The spec's phase 2 milestone
is "author a shot, play it back" (`spec:521-524`).

The editor is **not** in this phase; it is phase 2c (`spec:526-527`). Authoring happens through
a simple test window standing in for the editor. It is scaffolding for testing, not
product surface — the real flow is a button and a hotkey (`spec:375-377`), and a curve
editor (`spec:387-390`).

## Timing model

**Path and timing are separate** (`spec:229-233`). The control points and the spline through
them describe geometry only. A separate **timing curve** — position along the path, plotted
against time — carries every pacing decision (`spec:251-256`):

- steeper is faster, shallower is slower
- **a flat section is a hold**; dwelling needs no field on the point
- ease in and out are tangents at a key, not properties of a segment

A key's position is in **control-point units**: the whole part is the segment, the fraction
is how far along that segment's arc length (`spec:241-245`). Keys are anchored to points, so
moving or appending a point never retimes a shot (`spec:247-249`).

`Auto` tangents make a smooth pass-through the default (`spec:258-262`). At the ends they are
one-sided: the camera starts at full speed and stops dead. Easing is the user's choice, via a
`Flat` key (`spec:264-265`).

Looping is a **playback mode**, not a path shape: `Loop` hard-cuts from the last key back to
the first (`spec:278-283`). The path is always open. Future modes change only how elapsed time
is mapped, so all of it lives in A6.

## Part A — the Core layer

All in `CinematicCam.Core`: no Dalamud, no FFXIVClientStructs, no `unsafe`, no game. Every
test named is one the spec's list asks for (`spec:467-482`). I verify all of part A myself
on macOS before you see any of it.

### A1 — track model
`ControlPoint`, `Track`, `TimingKey`, `TangentMode`, `SnapPoint`, `AimMode`, `PlaybackMode` as
`spec:160-165` and `spec:235-239`. `AimMode` is `PathTangent` or `AimKeys` (`spec:200-207`);
there is no LookAt target. Per-point FoV (`spec:167`). `SnapPoint` stays distinct so
degenerate cases never reach the spline (`spec:300-303`).

`feat(core) add the track model`

### A2 — centripetal Catmull-Rom
Alpha 0.5 (`spec:173`). The curve passes through every control point (`spec:175-177`).
Endpoints duplicate for phantom points, and the path is always open (`spec:183-185`). Two
points is a straight dolly (`spec:183-184`). Coincident neighbours give a zero knot interval,
which is clamped rather than divided by.

Tests: curve passes through its control points; zero, one, two and coincident points do not
throw (`spec:479`).

`feat(core) add centripetal catmull-rom evaluation`

### A3 — arc-length table
Sample each segment, accumulate chord lengths, and map a key position — segment plus
fraction of that segment's arc length — to a spline parameter (`spec:193-196`). Rebuilds on
edit, not per frame.

Test: a bunched-then-spread track yields even spacing (`spec:468-469`).

`feat(core) evaluate the path by arc length`

### A4 — the timing curve
Monotone cubic Hermite through `TimingKey`s. `Auto` tangents from neighbours, limited so
the curve never decreases (`spec:267-270`) — a naive cubic overshoots and the camera
reverses. `Flat` pins tangents to zero. `Linear` and `Manual` are defined but unreachable
until the curve editor (`spec:261-262`).

A hold falls out of the limiter rather than needing its own code path: two keys at the same
position give a zero secant, the limiter zeroes both tangents, and the section is flat.
The same limiter is what eases a transition between legs of very different pace, because it
pulls the tangent at a junction toward the gentler of the two secants.

End keys use one-sided `Auto` tangents: full speed from the first frame, a
dead stop at the last (`spec:264-265`). Ends hold their value (`spec:272-274`), which is
where "a finished track holds its final frame" comes from rather than a special case. Total
length is the last key's time (`spec:276`). The curve knows nothing about playback modes;
looping is A6's job (`spec:278-283`).

Tests: a straight curve gives constant world speed within a segment on unevenly spaced
points (`spec:472-473`); a flat section holds the camera still (`spec:474`); the curve never
decreases, including for keys a naive cubic would overshoot (`spec:475-476`).

`feat(core) add the timing curve`

### A5 — aim modes
PathTangent with a pitch clamp, falling back to the nearest valid direction along the path
where coincident points collapse the derivative. The fallback reads only the place on the
path, never earlier frames, so scrubbing and playback agree (`spec:202-206`).

AimKeys on separate yaw and pitch channels, never slerped (`spec:207-211`). Yaw unwraps
before interpolation (`spec:213-215`). Yaw, pitch
and FoV are splined by the fraction of the segment's arc length travelled, so they sit still
during a hold without special handling (`spec:223-225`). Yaw and pitch become a look-at
through `FreeCamMotion.LookAtFrom`, so a captured aim replays exactly as it was flown.

Test: yaw crossing ±180° takes the short way (`spec:470`).

`feat(core) add the two aim modes`

### A6 — playback
`elapsed += dt` → evaluate the timing curve → look that position up on the arc-length table
→ position, aim and FoV. Accumulates a delta rather than counting frames so a shot runs the
same at 30 and 144fps (`spec:285-286`).

Evaluation is a pure `Evaluate(track, time)`, which `Tick` calls; the scrub bar needs the
same function in 2c (`spec:383-385`). Elapsed is a `double`.

The playback mode lives here and only here (`spec:278-283`). `Once` clamps elapsed at the
duration and holds. `Loop` wraps elapsed modulo the duration, so the camera cuts back to the
first frame; wrapping also keeps a track looping for hours at full precision.

Tests: position at t=5s matches under 60fps and 30fps delta sequences, within a float
tolerance — summing 1/60 three hundred times is not bit-identical to summing 1/30 a hundred
and fifty times (`spec:471`); a finished `Once` track holds its last frame (`spec:481`); a
`Loop` track cuts back to its first frame after its last key (`spec:478`).

`feat(core) play a track`

### A7 — key generation from legs and holds
The commands and, later, the editor need to write the curve without anyone typing tangents
(`spec:388-390`). Each control point gets a key at its own position; a hold adds a second key
at the same position. Point indices are 0-based.

- **Leg i** is the time from the last key at point i−1 to the first key at point i. Setting
  it shifts every later key by the difference. Default 5 seconds. Leg 0 is rejected.
- **Hold i** is the time between the two keys at point i. Setting it shifts every later key;
  zero removes the second key.
- **Appending** a point adds its key one default leg after the previous last key
  (`spec:247-249`).
- **Playback mode** is a field on the track; setting it never touches the keys.

Test: appending a control point does not retime the existing ones
(`spec:477`).

`feat(core) build timing keys from legs and holds`

### A8 — Director
`CameraState? Tick(float dt)` (`spec:290-292`). Non-null means we own the camera, null means
hands off (`spec:294-298`). Live mode off returns null. There is one live mode; playing a
track is live mode with that track on program (`spec:311-315`). A shot is a Track, a
SnapPoint, or GameCamera (`spec:300`).

The Director can pause: live stays on and `Tick` keeps returning the frame it stopped on.

Test: `Tick` returns null whenever live mode is off (`spec:482`).

`feat(core) add the director`

## Part B — wiring it to the game

### B1 — feed the Director to the camera
`Plugin.cs` sources camera state from free-cam or the `/ccam hold` test state. Add the
Director. The plugin is in one of three modes, and never two at once (`spec:88-93`):

| Mode | Camera source | Character lock and input blocking |
|---|---|---|
| Off | none | off |
| Editing | free-cam | on |
| Live | Director, playing or paused | on |

The user directs in editing mode and operates the camera in live mode; the character is
locked in both. `InputBlocker` and `MovementLock` therefore follow "editing or live" rather
than free-cam alone. Blocking zoom while live is what keeps the game from fighting our
position without writing `Distance` (`spec:107-110`, `spec:133-136`).

Moving between Editing and Live changes only the camera source: the lock and blocking stay held
and the pre-takeover snapshot is kept for release.

Every existing release path — zone change, area transition, logout, unload, `release` — must
turn live mode off and stop playback, not just drop the camera (`spec:347-354`).

`feat(camera) drive the camera from the director`

### B2 — test window
A plain ImGui window standing in for the 2c editor, because testing through chat commands is
too slow. Scaffolding, not product surface: nothing fancy, just functional. Nobody types a
tangent; everything writes keys via A7.

```
┌ Cinematic Cam (test) ───────────────────────────────┐
│ Mode: Editing          [Edit] [Play] [Stop] [Release] │
│ Aim: [Recorded aim ▾]   Playback: [Once ▾]            │
│ [Capture point]  [New track]                          │
│  #   Leg (s)   Hold (s)                               │
│  0      —       [0.0]                                 │
│  1    [5.0]     [3.0]                                 │
│ 3 points · total 18.0 s · playing 7.2 s               │
└───────────────────────────────────────────────────────┘
```

- **Edit** enters editing mode. From Off it takes the camera, free-cam starting at the game
  camera; from Live it starts free-cam at the current frame. Already editing: no-op.
- **Play** goes live from the start of the track; from Off or Editing it takes the camera.
  While live it restarts from zero.
- **Stop** pauses on the current frame and stays live.
- **Release** turns the plugin off and hands the camera back.
- **Capture point** appends the current camera as a control point (`spec:371-373`).
- **New track**, **Aim** (recorded aim / direction of travel), **Playback** (once / loop).
- **Point list**: one row per point with editable leg (none for point 0) and hold seconds.
- **Status line**: current mode, point count, total length, playback time.

Every control that changes the track works only in editing mode and is disabled while live,
paused included (`spec:316`), and while off. Invalid leg or hold input is rejected by A7's
validation and shown in the window, not applied.

Commands: `/ccam` opens the window. `/ccam release` stays as a text escape route
(`spec:361-362`). The `/ccam fly` command goes; the phase-1 debug commands (`hold`, `push`,
`nudge`, `reset`, `selftest`) stay as they are. Use Dalamud 15's windowing and ImGui bindings,
checked against the local Dalamud source (`~/code/Dalamud`, tag `15.0.3.5`).

`feat(ui) add a test window for tracks`

## Your test pass

One in-game round, at the end. The track lives in memory only, so a hot reload between
steps loses it.

1. `/ccam` opens the window. **Edit**, **New track**
2. Fly to a spot, **Capture point**. Repeat twice more. Clicking in the window does not
   also turn the free-cam
3. **Play** — the camera starts at full speed, crosses all three points **smoothly** with no
   stop or lurch at the middle one, and **stops dead on the last point and holds there**
   rather than snapping back
4. While it plays: WASD does not move the character, the scroll wheel does not zoom, chat
   still opens, and every editing control is disabled
5. **Edit**, set point 2's leg to 10, **Play**. The second transition is much slower and eases
   into the slower pace, not switching abruptly
6. **Edit**, set point 1's hold to 3, **Play**. It should slow to a stop on the middle point,
   wait three seconds, then move on
7. **Stop** mid-shot — the camera freezes. **Play** restarts from the first point
8. **Edit** — free-cam starts from where the camera is. **Capture point** a fourth point; the
   list shows the two existing legs and the hold with their timings unchanged
9. Playback **Loop**, **Play** — after the last point the camera cuts straight back to the
   first and plays again
10. **Release** — normal camera and movement return. `/ccam release` in chat does the same
11. **Play** and change zone mid-shot — playback stops and the camera releases

Steps 3, 5 and 6 are the ones only you can judge: whether the motion looks smooth.

**Result, 2026-09-21:** every step passed. Curve shaping felt rough, put down to having no
visual editor to see and adjust the path. Follow-up: Play now resumes after Stop, and a
Restart button starts over.

## Choices the spec does not make

Flagging rather than burying. Say if you would rather decide any of these.

- **Default aim mode: AimKeys.** Capture records yaw and pitch (`spec:371-373`), so
  replaying what you looked at is the natural default for a track built by flying.
- **Pitch clamp for PathTangent: ±89°** (`spec:202-203` asks for a clamp, no number).
- **Arc-length sampling: 60 samples per segment**, implied by "roughly 600 samples" for ten
  points (`spec:195-196`).
- **A track with no points: `Tick` returns null.** The spec requires only that it not throw
  (`spec:479`); null follows from "null means hands off".
- **A track with one point: the camera sits at that point** with its aim and FoV.
- **A zero-length segment** — two points in the same place — has no arc length to take a
  fraction of, so it uses the spline parameter instead. The camera stays put for the leg
  while AimKeys still turns it.
- **Auto-tangent limiter: Fritsch–Carlson.** The spec requires monotonicity (`spec:267-270`)
  without naming a method. This is the standard one.

Decided on 2026-09-21: 5 seconds per leg by default; full speed at the start and a dead stop
at the end; keys anchored to control points; Stop pauses, Play resumes (restarts when finished or coming from editing), Restart starts
over, Edit starts free-cam
from the current frame; nothing can be edited while live; a simple test window replaces the
chat commands, with `/ccam release` kept as an escape.

## Out of scope

| | Why |
|---|---|
| Curve editor — draggable keys and tangent handles | phase 2c (`spec:526-527`). Until then the curve is generated, not shaped |
| The rest of the editor UI — windows, scrub bar, 3D overlay, click-to-select, ImGuizmo | phase 2c; the gizmo convention check is its first task (`spec:420-425`) |
| Switchboard: slots, program/preview, TAKE, hotkeys | phase 3 (`spec:529-530`) |
| Persistence, and its config round-trip test | phase 3 (`spec:529-530`) |
| LookAt aim | deferred from v1 (`spec:34`) |
| Controller | unsupported |

## Risks

- **Part A is low risk.** No game, no interop; failures surface as failing tests.
- **The monotonicity limiter is the one subtle bit.** Get it wrong and the camera judders
  backwards at a key. It has a dedicated test (`spec:475-476`).
- **Part B reuses the camera ownership proven in phase 1.** The new failure mode is a shot
  that looks wrong rather than a crash. The one new behaviour is input blocking during live
  mode, checked by step 4.
- **`Tick` runs inside the camera `Update` hook.** Phase 1 measured that hook at roughly one
  call per frame; if it ran more often, playback would run fast.
- **Distance stays unwritten.** Nothing here touches `Camera.Distance`; the persistence
  hazard is unchanged.
