# Vista — Playback Direction (Phase 3.a)

Date: 2026-09-22
Status: Approved design, awaiting spec review

This adds Reverse and Ping-pong playback. It replaces the track's `PlaybackMode` with two
settings: a **Direction** and a **Loop** toggle. Where this differs from
`2026-09-20-cinematic-cam-design.md` (Playback mode) or `2026-09-21-editor-design.md` (the
second row), this document wins.

The plugin is unreleased, so the stored format changes with no migration.

## Terms

- **Shot time**: where the camera is in the shot, from 0 to the shot's length. The shot's
  length is the last compiled key's time, as today.
- **Clock**: seconds of playback since the current cycle began. Only `TrackPlayback` sees it.
- **Cycle**: one run of the shot: the shot's length for Forward and Reverse, twice it for
  Ping-pong.

## What a track stores

`Playback: PlaybackMode` goes. In its place:

- **Direction**: Forward, Reverse or Ping-pong. Default Forward.
- **Loop**: true or false. Default false.

Changing either is one undo step.

## Playback

Shot time is a pure function of the clock, with `L` the shot's length:

| Direction | Cycle | Shot time at clock `c` |
|---|---|---|
| Forward | `L` | `c` |
| Reverse | `L` | `L − c` |
| Ping-pong | `2L` | `c` for `c ≤ L`, then `2L − c` |

- **Loop off:** the clock stops at the end of the cycle and the shot finishes. A Forward
  shot holds its last frame; Reverse and Ping-pong hold the first.
- **Loop on:** the clock wraps at the end of the cycle and the shot never finishes.
  Forward and Reverse cut hard back to their start, as Loop does today. Ping-pong turns
  round at both ends, so it has no cut.
- **Holds** play as the shot time passes through them, in either direction. A Ping-pong
  turnaround therefore stays for twice the hold: once at the end of the outward pass, once
  at the start of the return (decided 2026-09-22). This applies at the last point on every
  round trip, and at the first point each time a looping Ping-pong turns back.
- **Easing** mirrors on a reversed pass: an ease-in plays as an ease-out.
- **Aim** plays as recorded. Under Aim along the path, a reversed pass flies backwards
  still facing the path's forward direction, like a rewind; so does Ping-pong's return
  (decided 2026-09-22).
- The clock accumulates frame time, as today, so a shot runs identically at any frame rate.
  A negative frame time never runs the clock backwards, and a zero-length shot stays at 0.

## Starting, seeking and finishing

- **Restart and Live's cue** start the clock at 0. A Reverse shot therefore starts at shot
  time `L`, and the scrub head walks back to 0.
- **Play on a finished shot** restarts it, as today.
- **Seek** takes a shot time and clamps it to 0 to `L`.
  - In Forward and Reverse it sets the clock that gives that shot time.
  - In Ping-pong it keeps whichever pass is running: on the outward pass (clock up to `L`)
    the clock becomes the shot time; on the return pass it becomes `2L` minus the shot
    time. A finished Ping-pong shot is on its return pass.
  - With Loop off, a seek that lands on the end of the cycle finishes the shot, and a seek
    back from a finished shot un-finishes it, as today.
- **The scrub bar** keeps its range of 0 to `L` and shows the shot time, both while
  editing and live (decided 2026-09-22).
- **Edit from Live** puts the edit-mode scrub head at the shot time.

## UI

**Track editor, second row.** The Playback drop-down becomes **Direction**, showing its
setting ("Direction: Forward", "Direction: Reverse", "Direction: Ping-pong") like the Aim
drop-down. A **loop icon button** follows it, using `FontAwesomeIcon.Repeat`:

- Lit when the track loops, greyed when it plays once (decided 2026-09-22).
- The tooltip names what a click does, as the pin's does: "Play once" when lit, "Loop" when
  greyed.

Both are disabled while live, paused included, like every other track setting. The track
editor's minimum width grows to fit the icon.

Nothing else changes. The scrub bar and the Timing window's playhead already show the
shot time, and the Play/Pause and Restart icons keep their behaviour.

## Architecture

**Core**

- `PlaybackMode` is removed. A `PlaybackDirection` enum and a `Loop` flag go on `Track`.
- A pure mapping from the clock, the direction and the shot's length to the shot time and
  the cycle length, as its own unit.
- `TrackPlayback` keeps the clock. `Elapsed` becomes `ShotTime`; `IsFinished`, `Seek` and
  `Restart` follow the rules above.
- `Director.Elapsed` becomes `ShotTime`. `SessionState` reads it for the scrub head and for
  Edit from Live.
- `TrackEditing.SetPlayback` becomes `SetDirection` and `SetLoop`.

**Plugin**

- The track editor's Direction drop-down and loop icon.

## Testing

Core under TDD:

- the mapping for each direction, with Loop on and off: the cycle length, shot time at the
  cycle's edges, and finishing;
- Reverse and Ping-pong finishing on the first frame, Forward on the last;
- a Ping-pong turnaround staying for twice the hold;
- Seek in each direction, and Ping-pong's Seek keeping the pass;
- a zero-length shot, a negative frame time, and 30 against 60 fps;
- the existing Once and Loop tests, carried to Forward with Loop off and on;
- the session: a Reverse cue at shot time `L`, Edit from Live taking the shot time, and
  scrubbing a live Ping-pong shot.

Then one `CHECKLIST.md` at the end, for the user to work through.
