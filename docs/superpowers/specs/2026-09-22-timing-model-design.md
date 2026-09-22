# Vista — Speed-Driven Timing and the Rename

Date: 2026-09-22
Status: Approved

This changes how a shot's timing is authored. Legs now take their pace from a speed
instead of a hand-typed time, and timing keys are compiled from the legs, not stored.
It also renames the plugin from Cinematic Cam to Vista.

It builds on `2026-09-22-curve-editor-design.md` (2c-2) and replaces its timing
authoring: leg times as the source of truth, keys between points, and hand-edited key
times. The 2c-2 per-side tangent modes, easing presets, graph and Timing window stay.
Where the two differ, this document wins.

The plugin is unreleased, so formats and defaults change freely.

## Terms

- **Track speed** — the pace, in yalms per second, of every leg that follows the track.
- **Pinned leg** — a leg with its own speed, set by hand.
- **Leg length** — the leg's distance along the path as timing measures it, at least
  0.1 m, as today.

## What a track stores

- The **track speed**.
- **Per leg:** whether it follows the track or is pinned, and if pinned its speed; its
  easing (the two bounding sides' tangent modes, and any Manual handle values).
- **Per point:** its hold, as today.

Timing keys are **compiled** from these, never stored or edited directly: one key per
point, at the time the legs and holds before it add up to, plus a hold end where a
point holds. The compile step is its own unit, so free keyframes can later be added as
a second source of keys without redesigning the model.

A leg's duration is always derived: its length divided by its speed, at least 0.1 s.
A leg's average speed is its length divided by that duration.

Defaults: a new track's speed is 2 yalms per second.

## Rules

| Action | Result |
|---|---|
| Set track **Speed** | Unpinned legs follow it; pinned legs keep their own speed |
| Set track **Duration** (the whole shot, holds included) | Vista sets the track speed so unpinned legs fill the time left after holds and pinned legs; the shortest it allows is what holds and pinned legs take, plus each unpinned leg at the 100 yalms per second speed limit (and at least 0.1 s) |
| Set a leg's **Duration** or **Speed** | That leg is pinned at the matching speed |
| Reset a leg | It follows the track speed again |
| Move a point (overwrite, gizmo, Point window fields) | Every leg keeps its speed, so durations follow the new lengths and the shot's length can change |
| Add to end | The new leg follows the track speed |
| Add after selected | Both halves of the split leg keep the old leg's speed and pin; the old leg's out side goes to the first half, its in side to the second, and the new point's sides are Auto |
| Delete a middle point | The merged leg keeps the first leg's speed and pin, the first leg's out side and the second leg's in side |
| Delete the first or last point | Its leg goes |
| Reorder | Leg speeds, pins and easing stay in their slots; holds travel with their point |
| Drag a point's key in the graph | Time moves between the leg before it and whatever follows (the next leg, or the point's hold); the shot's length is unchanged, and each leg whose time changed becomes pinned at its new speed. The first key is fixed; the last key has nothing after it, so dragging it changes the last leg's time and the shot's length |
| Drag a hold end | The hold changes; later keys shift and the shot's length changes |
| Easing | Unchanged: it spreads motion inside the leg's fixed duration |
| A dragged handle (Custom) | Keeps its shape through every edit: it is stored as a multiple of its span's average speed, so speed, duration, pin and point edits never reshape it (decided 2026-09-22) |

When every leg is pinned, the track Speed and Duration fields are disabled.

Ranges, clamped not refused: track and leg speed 0.01 to 100 yalms per second; leg
duration 0.1 to 600 s; track duration from its minimum above to 3600 s; hold 0 to 600 s.
A leg whose duration would pass 600 s at its speed is held at 600 s.

## UI

**Track editor.**

- The second row gains **Speed** (yalms per second) and **Duration** (s) fields beside
  Aim and Playback. Both apply when editing finishes, as the Leg field does today.
- Each point row shows the leg arriving at it: **Duration** (s) and **Speed** fields,
  coupled, then **Hold**. The Speed field shows the leg's actual average speed, its
  length over its duration, so the two always agree even at a limit; typing into it
  pins the leg at that speed (decided 2026-09-22). Row 1 has no leg fields.
- Every row from 2 on has a pin icon button. A pinned leg's pin is lit; clicking it
  unpins the leg, which follows the track speed again. An unpinned leg's pin is greyed;
  clicking it pins the leg at its current speed (decided 2026-09-22).

**Timing window.** A yalm scale runs down the graph's right edge: tick marks at a
round step (1, 2 or 5 times a power of ten), at most about six, labelled in yalms.
Hovering the plot, when nothing is being dragged, shows a readout of the time, the
distance and the speed at that moment, with a marker on the curve (decided
2026-09-22). The graph, the per-leg easing drop-down, Custom, the key buttons,
handles and Break and Unify all stay. A point key drag follows the rule above. Hold end
drags change the hold. Keys between points are removed: no diamonds, no double-click
to add, and no Delete key in the key menu. Remove hold stays.

## Rename to Vista

- Namespaces, projects and folders: `CinematicCam.Core`, `CinematicCam.Plugin` and
  `CinematicCam.Tests` become `Vista.Core`, `Vista.Plugin` and `Vista.Tests`.
- The assembly and manifest: `Vista.dll`, `InternalName` and `Name` Vista.
- The slash command `/ccam` becomes `/vista`. Window titles, window IDs, drag payload IDs
  and log prefixes follow (`[ccam]` becomes `[vista]`).
- Docs, CLAUDE.md, the README and build scripts follow. Past specs and plans keep their
  history and aren't rewritten, beyond a note at the top of the 2c-2 spec.
- In game, the user re-points Dev Tools at `Vista.dll`.
- **Last:** the repo folder and the GitHub repository are renamed. The folder rename
  moves the working directory and the assistant's memory path, so the user runs it
  from given commands after everything is committed. The GitHub rename is confirmed
  with the user before it runs.

No official Dalamud plugin is named Vista (checked against goatcorp/DalamudPluginsD17's
stable and testing lists, 2026-09-22).

## What leaves the 2c-2 work

- **Stays:** per-side tangent modes, `LegEasing`, the evaluator's distance and slope
  queries, `TimingGraph`, the Timing window's view, selection, scrubbing, easing and
  handles, and the session's timing selection and live edits.
- **Goes:** keys between points (`AddInnerKey`, their remapping in point edits, the
  diamonds, `DeleteKey` for inner keys), `TimingEditing.MoveKey` on stored key times,
  and the track editor's Leg field as a typed time.
- The final-review fixes to inner-key code are superseded.

## Architecture

**Core**

- `Track` stores the track speed, per-leg timing (pin and speed, easing sides) and
  per-point holds, in place of a stored key list.
- A new `TimingCompiler` builds the timing keys from a track and its leg lengths.
  `TrackEvaluator` uses it, and so does everything that reads keys.
- `TrackEditing` applies the rules table to the new model.
- `LegEasing` works on the per-leg easing sides.

**Plugin**

- The track editor gains the Speed and Duration fields, the per-row Duration and Speed
  fields, and the pin icon.
- The Timing window drops inner keys, and its point-key drag calls the pinning rule.

## Testing

Core under TDD:
- the compile step, and durations from speeds;
- each rule in the table, with pinned and unpinned legs and holds;
- track Duration solving for speed, with its minimum and the all-pinned case;
- the ranges;
- easing through the new model.

Then one `CHECKLIST.md` at the end, for the user to work through.
