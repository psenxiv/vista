# Vista — ripple edit in the timing graph (Phase 4.6)

Date: 2026-09-23
Status: Approved

Holding Ctrl while dragging a timing key moves every later key with it, lengthening or shortening
the track, instead of stealing time from the next leg.

## The two edits

Dragging key *k* today is a **trim**. `TimingEditing.MoveKey` re-speeds the leg before the key
*and* the leg after it, so the following key stays where it is and the track's total duration
never changes.

Ctrl makes it a **ripple**. Only the leg before the key is re-sped. Every later key keeps its own
duration and shifts by the same delta, so the track gets longer or shorter by exactly the drag.

For keys at 0, 5, 10 with two five-second legs, dragging the middle key to 7 gives:

| | key times | leg 1 | leg 2 | total |
|---|---|---|---|---|
| trim, today | 0, 7, 10 | 7 s | 3 s | 10 s |
| ripple, Ctrl | 0, 7, 12 | 7 s | 5 s | 12 s |

## Why it is the simpler of the two

A key's time is the sum of the leg durations before it, so shifting the later keys needs no work:
re-speeding leg *k* moves every subsequent key by the delta on its own. `RippleKey` is therefore
`MoveKey` with the second leg's re-speed and the next key's constraints removed.

The same holds for a hold. A hold end sits a fixed duration after its point, so it travels with
the key it belongs to without being touched.

## Clamping

`MoveKey` clamps the target to whichever is tighter: the dragged leg's own speed range, or what
the next leg can absorb. `RippleKey` drops the second of those, since the next leg is not being
changed, and clamps only to the dragged leg's range — `LegRange(length)`, the durations that leg
can have between `MinSpeed` and `MaxSpeed`.

Key 0 cannot move, and a non-finite time is ignored, exactly as in `MoveKey`.

## Plumbing

- `TimingEditing.RippleKey(track, evaluator, key, time)` joins `MoveKey` in Core.
- `SessionState.PreviewKeyMove(int key, float time, bool ripple = false)` picks between them.
  One live edit either way, so a ripple drag is one undo step like any other.
- `TimingWindow`'s `Drag` records whether Ctrl was held **when the drag began**, and passes that
  for the whole drag. Sampling it per frame would let the edit change kind halfway through a
  drag, which is not something a user can undo their way out of cleanly.

## Not doing

- No modifier for the handle drags. Ctrl applies to key drags only.
- No cap on the track's total duration beyond the per-leg speed range that already applies.
- No on-screen indication that Ctrl is held; the curve reshaping is the feedback.

## Testing

Core, with expected key times worked out from the leg arithmetic rather than by calling the
editor: the three-point fixture has points at x = 0, 10, 20 at 2 yalms per second, so its legs are
10 yalms each, five seconds each, and its keys sit at 0, 5 and 10.

- Rippling key 1 to 7 s puts the keys at 0, 7 and 12: leg 1 becomes 10 / 7 yalms per second and
  leg 2 is untouched at 2, so it still takes five seconds.
- The same drag through `MoveKey` leaves the keys at 0, 7, 10, which pins the difference between
  the two edits.
- Rippling the last key changes only the track's length, since nothing follows it.
- Key 0 refuses to move, and a non-finite time is ignored.
- A target beyond the leg's speed range clamps to that range.

The Ctrl binding itself is plugin-side and goes on the Phase 4 in-game checklist.
