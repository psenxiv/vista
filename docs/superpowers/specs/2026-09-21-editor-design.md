# Cinematic Cam — Editor (Phase 2c-1) Design

Date: 2026-09-21
Status: Approved in brainstorming, awaiting written-spec review

Phase 2c is built in two cycles. **2c-1**, this document, is the editor minus the
curve editor. **2c-2**, the curve editor, is planned after 2c-1 is built.

The main design is `2026-09-20-cinematic-cam-design.md`. This document refines its
Editor UI section; where they differ, this one wins.

## Terms

- **Control point** (short: **point**) — a stored camera pose on a track.
- **Add** — put the current camera on the track as a new point.
- **Overwrite** — replace the selected point's pose with the current camera,
  keeping its timing.

## Windows

The test window is removed. 2c-1 has two windows; the library and the switchboard
arrive in phase 3.

**Track editor** (resizable):

```
┌ Cinematic Cam ─────────────────────────────────────────────┐
│ Mode: Editing   [Edit] [Play] [Restart] [Stop] [Release]   │
│ Fly speed [──●──] 1x        [↶ Undo] [↷ Redo]              │
│ Aim [Recorded aim ▾]   Playback [Once ▾]   [New track]     │
├────────────────────────────────────────────────────────────┤
│ [ + Add │ ▾ ]                                              │
│  #    Leg (s)  Hold (s)                                    │
│  ≡ 1     –       0.0                                       │
│  ≡ 2    5.0      0.0     ◀                                 │
│  ≡ 3    5.0      2.0                                       │
├────────────────────────────────────────────────────────────┤
│ 0.0 ━━━━━━━━━━━━●━━━━━━━━━━━━━━━━━━━━━━━━━ 12.0 s   6.3 s  │
│ 3 points | total 12.0 s | editing                          │
└────────────────────────────────────────────────────────────┘
```

- The mode, fly speed, aim, playback and New track controls carry over from the
  test window unchanged.
- **Add split button.** The `+ Add` half adds to the end. The `▾` half opens a
  menu with each item's shortcut on the right:

  | Item | Shortcut |
  |---|---|
  | Add to end | `` ` `` |
  | Add after selected | Alt + `` ` `` |
  | Overwrite selected | Ctrl + `` ` `` |

  The last two are disabled when nothing is selected. Overwrite has no
  confirmation step; undo covers it.
- **Point list.** Click a row to select it. Double-click a row to jump the camera
  to that point (see Scrub and jumps). Drag the `≡` handle to reorder. Leg and hold
  fields work as in the test window.
- **Scrub bar**, 0 to the shot's total length; see Scrub and jumps.

**Point window** (compact, auto-sized), shown only while a point is selected in
editing mode. It remembers where it was placed.

```
┌ Point 2 ──────────────────────────────┐
│ Gizmo  (•) Move  ( ) Rotate           │
│ X [ -137.1 ]  Y [ 3.3 ]  Z [ -154.3 ] │
│ Yaw [ 42.0° ]  Pitch [ -8.5° ]        │
│ Roll [ 0.0° ]  FoV [ 45.0° ]          │
│                            [ Delete ] │
└───────────────────────────────────────┘
```

Position is in yalms; yaw, pitch, roll and FoV in degrees, converted from the
stored radians for display and entry only. A field applies when editing finishes.

Everything that changes the track is disabled while live, paused included, as
today. The scrub bar stays usable while live.

## Overlay

Drawn in editing mode only, never live or off, on the ImGui **background** draw
list: over the game, under plugin windows. Projected with the game's
`WorldToScreen`; anything behind the camera is skipped.

- **Path** — the spline sampled densely along its length, drawn as a polyline.
- **Markers** — a numbered circle per point; the selected point is highlighted.
- **Aim lines** — a short line from each point along its recorded aim, drawn only
  in Recorded-aim mode.

Colours live in one place.

## Selection

- Left-click on a marker selects it. A click is a press and release that moves
  under a few pixels; a drag still turns the camera. Overlapping markers: the
  nearest to the cursor wins.
- Left-click on empty space deselects.
- Clicks over plugin windows are ignored.
- A click on a marker does not reach the game, so it cannot target anything.

Selection after track changes:

| Change | Selection |
|---|---|
| Add to end | unchanged |
| Add after selected | the new point |
| Overwrite selected | unchanged |
| Delete | none |
| Reorder | stays on the same point, wherever it ends up |
| Undo / redo | restored to what it was at that step |

## Gizmo

On the selected point only, via `Dalamud.Bindings.ImGuizmo`. No scale, no
snapping.

- **Move** — axis arrows and plane handles along world axes.
- **Rotate** — rings in the point's own frame: yaw and pitch in Recorded-aim mode,
  roll in both aim modes. In Direction-of-travel mode only the roll ring shows.
- The mode is switched in the Point window, or with **R** while a point is
  selected.
- One drag is one undo step, committed on release.
- Gizmo drags do not reach the game, so the camera does not turn while dragging.

**Drawing and matrices.** BDTHPlugin (reference only, no licence) draws its gizmo
in a transparent, input-less, full-screen ImGui window on the main viewport, and
fixes up the game's matrices before handing them to ImGuizmo: from the render
camera's near and far planes it rewrites projection `M33` and `M43`, and sets view
`M44` to 1. 2c-1 reimplements that against ClientStructs, using the world camera
rather than the active one. Probe 1 confirms it.

## Timing rules

| Change | Timing |
|---|---|
| Add to end | new leg of the default 5 s |
| Add after selected | the leg it lands in splits in proportion to path length either side of the new point, so total length is unchanged; the new point has no hold; the selected point keeps its hold. After the last point it is Add to end |
| Delete, middle point | its incoming leg, hold and outgoing leg merge into one leg; total unchanged |
| Delete, first point | its hold and outgoing leg go |
| Delete, last point | its incoming leg and hold go |
| Delete, only point | the track becomes empty |
| Reorder | holds travel with their point; leg times stay in their slots |
| Overwrite, gizmo, numeric fields | timing unchanged; moving a point changes speed, not duration |

Path lengths for the split count every segment as at least 10 cm, as playback
timing does, so coincident points never divide by zero.

These operations assume one timing key per point, which is all 2c-1 can produce.
A track with keys between points is refused with a plain message; 2c-2 revisits
this.

## Undo

- Every track change is one step: add, overwrite, delete, reorder, gizmo drag,
  numeric field, leg, hold, aim mode, playback mode, New track.
- A new change clears redo.
- Editing mode only. History survives switching to live and back, and is lost on
  unload.
- The last 100 steps are kept.
- **Ctrl + Z** undoes, **Ctrl + Y** redoes; buttons in the track editor too.

## Scrub and jumps

**Editing.** Dragging the scrub bar shows the shot at that moment: position, roll
and FoV, and aim if probe 2 passes. On release the free-cam flies on from there.
The scrub head shows the last scrubbed time.

**Double-click a row** jumps the camera to that point instantly, on the same terms
as a scrub release.

**Live.** Dragging seeks: playback holds at the dragged moment while dragging, then
continues in its prior state, playing or paused. Seeking a finished `Once` shot
back un-finishes it. The scrub head shows playback time.

**Aim on scrub release, jumps and Edit from Live** — the free-cam's aim comes from
the game camera's `DirH`/`DirV`. If probe 2 proves they are runtime state, they are
written so the free-cam keeps the frame's aim. If not, position, roll and FoV carry
over and aim does not; that limitation is documented and reported before anything
builds on it.

## Keys and input

Editing mode:

| Input | Action |
|---|---|
| W A S D | fly |
| Space / C | up / down (C replaces Ctrl) |
| Shift | ×4 speed |
| Q / E | roll |
| Scroll wheel | fly speed step |
| Mouse drag | look (the game's own camera drag) |
| `` ` `` | add to end |
| Alt + `` ` `` | add after selected |
| Ctrl + `` ` `` | overwrite selected |
| R | gizmo Move ⇄ Rotate, only with a point selected |
| Ctrl + Z / Ctrl + Y | undo / redo |
| Click marker / empty space | select / deselect |
| Double-click row | jump to point |

Live mode is unchanged: Escape brings the UI back; the scrub bar seeks.

- Our keys do nothing while typing in chat or in a plugin text field, so Ctrl + Z
  in a number field undoes the text, not the track.
- In editing mode the keys and chords above are hidden from the game, so C does
  not open the Character window and whatever R, backtick, Z and Y are bound to does
  not fire. Movement keys stay blocked as today. Ctrl alone is no longer blocked.

## Probes

Built first, each its own in-game check with a stated pass condition, before
anything depends on it.

1. **Gizmo convention.** A gizmo on one point using the matrix fix-up above.
   Pass: dragging each arrow moves the point along the arrow as drawn, and the log
   shows only that axis changing.
2. **`DirH`/`DirV` safety.** Read the source for what they are, then write
   distinctive angles and zone, log out and in, and use the settings screen.
   Pass: nothing reaches the saved camera.
3. **Keys and clicks.** C, R, backtick and Ctrl + Z / Y can be hidden from the
   game; Alt (Option under Wine) arrives as Alt; a marker click does not target
   anything. If a key cannot be hidden reliably, the alternatives go to the user.

## Architecture

Core decides; the plugin carries it out.

**Core**, tested without the game:

- `TrackEditing` gains insert, delete, move and replace, with the timing rules
  above.
- Undo history of `Track` values; `SessionState.ChangeTrack` records each change;
  undo and redo on `SessionState`, editing only.
- Selection in `SessionState`, kept valid across changes per the table above.
- `Director` seek, for scrubbing while live.
- Click hit-testing: nearest projected marker within a radius.

**Plugin**, verified in game:

- Track editor window and Point window, replacing the test window.
- Overlay, gizmo, and editor input: bindings, hiding our keys from the game, and
  keeping marker and gizmo clicks from the game.
- Free-cam: fly-down on C; setting its pose for scrub, jumps and Edit from Live.

**Order:** the three probes, Core operations, overlay and selection, gizmo, the two
windows, scrub and jumps, undo.

## Testing

Core under TDD: insert, delete, move and replace with their timing; undo history;
selection validity; hit-testing; seek. In game, a short checklist after each piece
with at most one or two unverified changes per round.

## Open issues

- **Mouse-look ignores roll** (reported 2026-09-21). At roll 0, dragging right
  turns the view right; at 90° roll it turns the view up. Mouse-look drives the
  game's world-space `DirH`/`DirV`, which know nothing of our roll. A fix maps
  mouse deltas into the rolled frame and writes the angles, so it depends on
  probe 2. Not scheduled; the user decides when.
