# Phase 2 — tracks

Design: `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`.
Phase 1 and 1b are complete: the camera is owned, flies, and always releases.

**Goal, in your hands:** fly to three spots, press a key at each, set the pacing, and watch
the camera glide through all three and hold on the last frame. The spec's phase 2 milestone
is "author a shot, play it back" (`spec:529-532`).

The UI is **not** in this phase; it is phase 2c (`spec:534-535`). Authoring happens through
`/ccam track` subcommands standing in for the editor. They are scaffolding for testing, not
product surface — the real flow is a button and a hotkey (`spec:383-385`), and a curve
editor (`spec:395-398`).

## Timing model

**Path and timing are separate** (`spec:225-229`). The control points and the spline through
them describe geometry only. A separate **timing curve** — position along the path, plotted
against time — carries every pacing decision (`spec:248-253`):

- steeper is faster, shallower is slower
- **a flat section is a hold**; dwelling needs no field on the point
- ease in and out are tangents at a key, not properties of a segment

A key's position is in **control-point units**: the whole part is the segment, the fraction
is how far along that segment's arc length (`spec:237-241`). Keys are anchored to points, so
moving, appending or toggling loop never retimes a shot (`spec:243-246`).

`Auto` tangents make a smooth pass-through the default (`spec:255-259`). At the ends of an
open track they are one-sided: the camera starts at full speed and stops dead. Easing is the
user's choice, via a `Flat` key (`spec:261-263`).

## Part A — the Core layer

All in `CinematicCam.Core`: no Dalamud, no FFXIVClientStructs, no `unsafe`, no game. Every
test named is one the spec's list asks for (`spec:475-490`). I verify all of part A myself
on macOS before you see any of it.

### A1 — track model
`ControlPoint`, `Track`, `TimingKey`, `TangentMode`, `SnapPoint`, `AimMode` as
`spec:162-166` and `spec:231-235`. `AimMode` is `PathTangent` or `AimKeys` (`spec:201-208`);
there is no LookAt target. Per-point FoV (`spec:168`). `SnapPoint` stays distinct so
degenerate cases never reach the spline (`spec:297-300`).

`feat(core) add the track model`

### A2 — centripetal Catmull-Rom
Alpha 0.5 (`spec:174`). The curve passes through every control point (`spec:176-178`).
Endpoints duplicate for phantom points; looping tracks wrap (`spec:184-186`). Two points is
a straight dolly (`spec:184-185`). Coincident neighbours give a zero knot interval, which is
clamped rather than divided by.

Tests: curve passes through its control points; zero, one, two and coincident points do not
throw (`spec:487`).

`feat(core) add centripetal catmull-rom evaluation`

### A3 — arc-length table
Sample each segment, accumulate chord lengths, and map a key position — segment plus
fraction of that segment's arc length — to a spline parameter (`spec:194-197`). Rebuilds on
edit, not per frame.

Test: a bunched-then-spread track yields even spacing (`spec:476-477`).

`feat(core) evaluate the path by arc length`

### A4 — the timing curve
Monotone cubic Hermite through `TimingKey`s. `Auto` tangents from neighbours, limited so
the curve never decreases (`spec:265-268`) — a naive cubic overshoots and the camera
reverses. `Flat` pins tangents to zero. `Linear` and `Manual` are defined but unreachable
until the curve editor (`spec:258-259`).

A hold falls out of the limiter rather than needing its own code path: two keys at the same
position give a zero secant, the limiter zeroes both tangents, and the section is flat.
The same limiter is what eases a transition between legs of very different pace, because it
pulls the tangent at a junction toward the gentler of the two secants.

End keys of an open track use one-sided `Auto` tangents: full speed from the first frame, a
dead stop at the last (`spec:261-263`). Ends hold their value (`spec:270-272`), which is
where "a finished track holds its final frame" comes from rather than a special case. Total
length is the last key's time (`spec:274`). Looping wraps modulo total with cyclic auto
tangents at the seam (`spec:276-280`).

Tests: a straight curve gives constant world speed within a segment on unevenly spaced
points (`spec:480-481`); a flat section holds the camera still (`spec:482`); the curve never
decreases, including for keys a naive cubic would overshoot (`spec:483-484`); the loop seam
is continuous in position and speed (`spec:486`).

`feat(core) add the timing curve`

### A5 — aim modes
PathTangent with a pitch clamp, falling back to the nearest valid direction along the path
where coincident points collapse the derivative. The fallback reads only the place on the
path, never earlier frames, so scrubbing and playback agree (`spec:203-207`).

AimKeys on separate yaw and pitch channels, never slerped (`spec:208-212`). Yaw unwraps
before interpolation, including across a loop's closing segment (`spec:214-217`). Yaw, pitch
and FoV are splined by the fraction of the segment's arc length travelled, so they sit still
during a hold without special handling (`spec:219-221`). Yaw and pitch become a look-at
through `FreeCamMotion.LookAtFrom`, so a captured aim replays exactly as it was flown.

Test: yaw crossing ±180° takes the short way (`spec:478`).

`feat(core) add the two aim modes`

### A6 — playback
`elapsed += dt` → evaluate the timing curve → look that position up on the arc-length table
→ position, aim and FoV. Accumulates a delta rather than counting frames so a shot runs the
same at 30 and 144fps (`spec:282-283`).

Evaluation is a pure `Evaluate(track, time)`, which `Tick` calls; the scrub bar needs the
same function in 2c (`spec:391-393`). Elapsed is a `double`, and wraps on a loop, so a track
looping for hours at an event keeps its precision.

Tests: position at t=5s matches under 60fps and 30fps delta sequences, within a float
tolerance — summing 1/60 three hundred times is not bit-identical to summing 1/30 a hundred
and fifty times (`spec:479`); a finished track holds its last frame (`spec:489`).

`feat(core) play a track`

### A7 — key generation from legs and holds
The commands and, later, the editor need to write the curve without anyone typing tangents
(`spec:396-398`). Each control point gets a key at its own position; a hold adds a second key
at the same position. Point indices are 0-based.

- **Leg i** is the time from the last key at point i−1 to the first key at point i. Setting
  it shifts every later key by the difference. Default 5 seconds. Leg 0 is rejected on an
  open track; on a looping track it is the closing leg back to the first point.
- **Hold i** is the time between the two keys at point i. Setting it shifts every later key;
  zero removes the second key.
- **Appending** a point adds its key one default leg after the previous last key. On a
  looping track it inserts before the closing segment and renumbers the closing key
  (`spec:243-246`).
- **Loop on** adds a closing key one default leg after the last; **loop off** removes it.

Test: appending a control point or toggling loop does not retime the existing ones
(`spec:485`).

`feat(core) build timing keys from legs and holds`

### A8 — Director
`CameraState? Tick(float dt)` (`spec:287-289`). Non-null means we own the camera, null means
hands off (`spec:291-295`). Live mode off returns null. There is one live mode; playing a
track is live mode with that track on program (`spec:308-312`). A shot is a Track, a
SnapPoint, or GameCamera (`spec:297`).

The Director can pause: live stays on and `Tick` keeps returning the frame it stopped on.

Test: `Tick` returns null whenever live mode is off (`spec:490`).

`feat(core) add the director`

## Part B — wiring it to the game

### B1 — feed the Director to the camera
`Plugin.cs` sources camera state from free-cam or the `/ccam hold` test state. Add the
Director. The plugin is in one of three states, and never two at once:

| State | Camera source | Character lock and input blocking |
|---|---|---|
| Idle | none | off |
| Flying | free-cam | on |
| Live | Director, playing or paused | on |

The user directs while flying and operates the camera while live; the character is locked
in both (`spec:87-92`). `InputBlocker` and `MovementLock` therefore follow "flying or live"
rather than free-cam alone. Blocking zoom while live is what keeps the game from fighting our
position without writing `Distance` (`spec:106-109`, `spec:132-135`).

Moving from Flying to Live changes only the camera source: the lock and blocking stay held
and the pre-takeover snapshot is kept for release.

Every existing release path — zone change, area transition, logout, unload, `release` — must
turn live mode off and stop playback, not just drop the camera (`spec:340-347`).

`feat(camera) drive the camera from the director`

### B2 — authoring and playback commands
Scaffolding standing in for the editor. Nobody types a tangent; these write keys via A7.

- `/ccam track new`
- `/ccam track capture` — append the current camera as a control point (`spec:379-381`).
  Refused while live.
- `/ccam track leg <index> <seconds>` — how long the transition into that point takes
- `/ccam track hold <index> <seconds>` — a flat section at that point
- `/ccam track aim <tangent|keys>`, `/ccam track loop`
- `/ccam track play` — go live from zero; from Idle it takes the camera. While live it
  restarts from zero.
- `/ccam track stop` — pause on the current frame. `release` hands the camera back.
- `/ccam track info` — points, keys, legs, holds, total length

`/ccam fly` while live starts free-cam from the current frame.

`feat(track) add authoring and playback commands`

## Your test pass

One in-game round, at the end. The track lives in memory only, so a hot reload between
steps loses it.

1. `/ccam fly`, `/ccam track new`
2. Fly to a spot, `/ccam track capture`. Repeat twice more.
3. `/ccam track play` — the camera starts at full speed, crosses all three points
   **smoothly** with no stop or lurch at the middle one, and **stops dead on the last point
   and holds there** rather than snapping back
4. While it plays: WASD does not move the character, the scroll wheel does not zoom, and
   chat still opens
5. `/ccam track leg 2 10` — make the second transition much slower, play again. It should
   ease into the slower pace, not switch abruptly
6. `/ccam track hold 1 3` — play again. It should slow to a stop on the middle point, wait
   three seconds, then move on
7. Play, then `/ccam track stop` mid-shot — the camera freezes. `/ccam track play` restarts
   from the first point
8. `/ccam fly` — free-cam starts from where the camera is. `/ccam track capture` a fourth
   point, `/ccam track info` — the two existing legs and the hold keep their timings
9. `/ccam track loop`, play — the camera laps with no lurch at the seam
10. `/ccam release` — normal camera and movement return
11. Play again and change zone mid-shot — playback stops and the camera releases

Steps 3, 5, 6 and 9 are the ones only you can judge: whether the motion looks smooth.

## Choices the spec does not make

Flagging rather than burying. Say if you would rather decide any of these.

- **Default aim mode: AimKeys.** Capture records yaw and pitch (`spec:379-381`), so
  replaying what you looked at is the natural default for a track built by flying.
- **Pitch clamp for PathTangent: ±89°** (`spec:203-204` asks for a clamp, no number).
- **Arc-length sampling: 60 samples per segment**, implied by "roughly 600 samples" for ten
  points (`spec:196-197`).
- **A track with no points: `Tick` returns null.** The spec requires only that it not throw
  (`spec:487`); null follows from "null means hands off".
- **A track with one point: the camera sits at that point** with its aim and FoV.
- **A zero-length segment** — two points in the same place — has no arc length to take a
  fraction of, so it uses the spline parameter instead. The camera stays put for the leg
  while AimKeys still turns it.
- **Auto-tangent limiter: Fritsch–Carlson.** The spec requires monotonicity (`spec:265-268`)
  without naming a method. This is the standard one.

Decided on 2026-09-21: 5 seconds per leg by default; full speed at the start and a dead stop
at the end; keys anchored to control points; `stop` pauses, `play` restarts, `fly` starts from
the current frame, `capture` is refused while live.

## Out of scope

| | Why |
|---|---|
| Curve editor — draggable keys and tangent handles | phase 2c (`spec:534-535`). Until then the curve is generated, not shaped |
| The rest of the editor UI — windows, scrub bar, 3D overlay, click-to-select, ImGuizmo | phase 2c; the gizmo convention check is its first task (`spec:428-433`) |
| Switchboard: slots, program/preview, TAKE, hotkeys | phase 3 (`spec:537-538`) |
| Persistence, and its config round-trip test | phase 3 (`spec:537-538`) |
| LookAt aim | deferred from v1 (`spec:35`) |
| Controller | unsupported |

## Risks

- **Part A is low risk.** No game, no interop; failures surface as failing tests.
- **The monotonicity limiter is the one subtle bit.** Get it wrong and the camera judders
  backwards at a key. It has a dedicated test (`spec:483-484`).
- **Part B reuses the camera ownership proven in phase 1.** The new failure mode is a shot
  that looks wrong rather than a crash. The one new behaviour is input blocking during live
  mode, checked by step 4.
- **`Tick` runs inside the camera `Update` hook.** Phase 1 measured that hook at roughly one
  call per frame; if it ran more often, playback would run fast.
- **Distance stays unwritten.** Nothing here touches `Camera.Distance`; the persistence
  hazard is unchanged.
