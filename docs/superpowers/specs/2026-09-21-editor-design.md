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
│ [Edit ▾] [▶] [⏮]   [↶] [↷]   [ + Add │ ▾ ]                 │
│ [Aim: Recorded aim ▾] [Playback: Once ▾]              [🗑] │
├────────────────────────────────────────────────────────────┤
│  #    Leg (s)  Hold (s)                                    │
│  ≡ 1     –       0.0                                  [🗑] │
│  ≡ 2    5.0      0.0     (selected row highlighted)   [🗑] │
│  ≡ 3    5.0      2.0                                  [🗑] │
├────────────────────────────────────────────────────────────┤
│ [━━━━━━━━━━━━●━━━━━━━━━━  6.3 / 12.0 s ]   Speed [ 1x ]    │
└────────────────────────────────────────────────────────────┘
```

Layout revised by the user on 2026-09-22 after using the first build:

- **Top row.**
  - **Mode drop-down:** Off, Edit and Live, in that order. It replaces the Mode text and the Edit
    and Release buttons. Live plays the shot as Play did, and Off releases the
    camera.
  - **Play/Pause icon:** one icon that shows Pause while the shot is playing and
    Play otherwise; Play from Edit or Off goes live.
  - **Restart icon**, then **Undo** and **Redo** icons. Each icon has a tooltip
    naming it.
  - **Add split button.**
- **Second row:** Aim and Playback drop-downs that show their setting ("Aim: Recorded
  aim", "Playback: Once"), and **Clear track** as a trash icon at the right end.
- **Bottom row:** the scrub bar shows current and total time in the bar itself;
  **Speed** sits at its right, in Edit mode only. There is no status line: the
  mode drop-down and the Play/Pause icon show the state.
- Rows are spaced a little more loosely than ImGui's default.

Revised again on 2026-09-22 after the second build: whole-row selection, trash icons,
the compact second row, and the status line removed.
- **Add split button.** The `+ Add` half adds to the end. The `▾` half opens a
  menu with each item's shortcut on the right:

  | Item | Shortcut shown |
  |---|---|
  | Add to end | Backtick |
  | Add after selected | Alt + Backtick |
  | Overwrite selected | Ctrl + Backtick |

  The last two are disabled when nothing is selected. Overwrite has no
  confirmation step; undo covers it.
- **Point list.** Rows are numbered from 1, matching the markers. Click anywhere on a
  row to select it; the whole row highlights. Double-click a row to jump the camera
  to that point (see Scrub and jumps). Drag a row to reorder. Leg and hold fields
  work as in the test window. A trash icon at the end of each row deletes that point.
- **Scrub bar**, 0 to the shot's total length; see Scrub and jumps.

**Point window** (compact, auto-sized), shown only while a point is selected in
editing mode. It remembers where it was placed. It has no close button: deselecting
hides it, and Escape does not close it.

```
┌ Point 2 ──────────────────────────────────┐
│ Gizmo  (•) Move  ( ) Rotate  [⧉][📋][🗑] │
│ X     [ -137.1 ]  Y   [ 3.3 ]   Z    [ -154.3 ] │
│ Pitch [ -8.5° ]   Yaw [ 42.0° ] Roll [ 0.0° ]   │
│ FoV   [ 45.0° ]                                │
└───────────────────────────────────────────┘
```

The fields sit in an aligned grid, and Delete is a trash icon at the right of the
top row. Labels use the gizmo's axis colours: X and Pitch red, Y and Yaw green, Z and
Roll blue, since pitch turns about X, yaw about Y and roll about Z. So each colour
lines up down its column. FoV is uncoloured, on its own row.

The fields are drag fields, as in BDTHPlugin: drag left or right to change the value,
or double-click to type one. The point, its marker and the path move live while
dragging, and each drag is one undo step, like a gizmo drag. Undo mid-drag reverts
it. (Decided 2026-09-22. Leg and hold in the track editor stay typed fields.)

**Copy and paste** icons sit beside the trash icon. Copy takes the point's position,
yaw, pitch, roll and FoV into a clipboard inside the plugin; paste writes them onto
the selected point as one undo step, keeping its leg and hold. Paste is disabled
until something is copied, and the copy is forgotten when the plugin unloads.
(Decided 2026-09-22.)

Position is in yalms; yaw, pitch, roll and FoV in degrees, converted from the
stored radians for display and entry only. The Point window's fields apply live (see
below). The track editor's leg and hold fields apply when editing finishes, including
when the window closes or a mode button is pressed first; ImGui never reports a
closed window's field losing focus.
In Direction-of-travel mode the Yaw and Pitch fields show the stored values but are
disabled, matching the gizmo's roll-only ring.

**Invalid input is prevented, not reported** (decided 2026-09-21). Buttons and menu
items that cannot act are disabled, keys that cannot act do nothing, and number
fields clamp to a sensible range instead of refusing:

| Field | Range |
|---|---|
| Leg | 0.1 to 600 s |
| Hold | 0 to 600 s |
| Pitch | −89° to +89°, as the gizmo |
| Yaw, roll | wrapped to −180° to +180° |
| FoV | the game's own `Camera.MinFoV` to `Camera.MaxFoV` |
| X, Y, Z | any finite value |

There is no error line. Anything still refused, such as an unreadable camera, is
logged as a warning.

Closing the track editor hides only the window. The overlay, gizmo and editor keys
belong to editing mode and keep working.

Everything that changes the track is disabled while live, paused included, as
today. The scrub bar stays usable while live.

## Overlay

Drawn in editing mode only, never live or off, on the ImGui **background** draw
list: over the game, under plugin windows. Projected with our own projection
from the world camera's matrices, not `IGameGui.WorldToScreen`, which lags a frame
(probe 1). Markers behind the camera are skipped; the path is clipped at the near
plane.

- **Path** — the spline sampled densely along its length, drawn as a polyline.
- **Markers** — a numbered circle per point, 20 px in radius with the number sized
  to match; the selected point is highlighted.
- **Aim arrows** — a short arrow, with a head, from each point along its recorded
  aim, drawn only in Recorded-aim mode.
- **Up arrows** — a shorter, light-blue arrow from each point along the camera's up
  direction there, so roll is visible. Drawn with the aim arrows, in Recorded-aim
  mode only. (Added 2026-09-22.)

Colours live in one place.

## Selection

- Left-click on a marker selects it. A click is a press and release that moves
  under a few pixels; a drag still turns the camera. Overlapping markers: the
  nearest to the cursor wins.
- Left-click on empty space deselects. A press that turns the camera is a drag,
  not a click, even if the cursor stays put: the game locks the cursor while the
  camera is dragged.
- Clicks over plugin windows are ignored. Clicks on the game's own HUD count as
  empty space and deselect.
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
- **Rotate** — gimbal rings, each changing one angle: the yaw ring lies flat
  around world up, the pitch ring turns only with yaw, and the roll ring faces
  along the aim. Yaw and pitch show in Recorded-aim mode, roll in both aim modes.
  In Direction-of-travel mode only the roll ring shows. (Chosen 2026-09-21 after
  in-game testing: rings in the point's own frame re-oriented after every drag
  and made aiming hard.)
- The mode is switched in the Point window, or with **R** while a point is
  selected.
- One drag is one undo step, committed on release. While dragging, the path and
  the point's marker follow the drag.
- Gizmo drags do not reach the game, so the camera does not turn while dragging.
- The arrows keep a fixed direction; they do not flip to face the camera
  (`ImGuizmo.AllowAxisFlip(false)`).
- The gizmo scales with distance. A size correction was built and reverted on
  2026-09-21: the user accepted the scaling.

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
  numeric field, leg, hold, aim mode, playback mode, Clear track.
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
| Ctrl + Alt + `` ` `` | nothing (AltGr sends it on some layouts) |
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
- The left mouse button is read from its physical state too: Dalamud passes a
  press to ImGui only while ImGui wants the mouse, and clears ImGui's buttons
  otherwise (`Win32InputHandler.cs` 209–215, 292–301), so ImGui never sees a
  press on empty space.

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

Results (2026-09-21, game 7.56hf2):

- **Probe 1 passed.** Markers projected with the game's `SceneCamera.ViewMatrix`,
  or with a view built from the frame we write, both sit on the target while
  flying; Dalamud's `IGameGui.WorldToScreen` lags a frame, so the editor uses its
  own projection. With BDTHPlugin's fix-up every gizmo drag moved along its own
  axis only.
- **Probe 2 passed.** Writing `DirH`/`DirV` took effect and held, mouse-look
  continued from it, and after zoning, the settings screen and a logout,
  `COMMON.DAT`, `CONTROL0.DAT` and `CONTROL1.DAT` were byte-identical. They are
  runtime state: scrub release, jumps and Edit from Live keep the frame's aim.
- **Probe 3, first run.** Clearing a key in the game's key buffer (Dalamud
  `IKeyState`) stops its game action, and making ImGui want the mouse over a
  marker stops the click targeting. Reading our keys failed: Dalamud releases
  every non-modifier key in ImGui each frame (`Win32InputHandler.cs:590`), and a
  cleared key stays cleared in the game buffer while held. A second run reads
  the physical state with `GetAsyncKeyState`.
- **Probe 3, second run: passed.** `GetAsyncKeyState` reports our keys and
  Alt correctly under Wine: C held for 2 s read as down for all 180 frames while
  the game buffer saw it for 1, and the Character window stayed shut. The editor
  reads keys from their physical state and hides them by clearing the game
  buffer.

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
selection validity; hit-testing; seek. In game, one checklist at the end of each
plan run.

## Part 2a results (2026-09-21)

In game, all fly-down, overlay, key and gizmo checks passed, including undo in the
middle of a gizmo drag. Clicking empty space did not deselect: see the mouse note
under Keys and input. Changes asked for: markers twice the size, aim arrows
instead of lines, gimbal rotate rings, and Ctrl + Alt + backtick doing nothing.
The follow-ups passed in game the same day: deselecting, larger markers, aim
arrows, gimbal rings, Ctrl + Alt + backtick, and leg and hold edits applying on close
and before mode changes. A click on the game's own HUD deselects, and the user
accepted that.

## Open issues

- **Mouse-look ignores roll** (reported 2026-09-21). At roll 0, dragging right
  turns the view right; at 90° roll it turns the view up. Mouse-look drives the
  game's world-space `DirH`/`DirV`, which know nothing of our roll. A fix maps
  mouse deltas into the rolled frame and writes the angles, so it depended on
  probe 2. Fixed after probe 2 passed: each frame the free-cam takes the game's
  yaw and pitch change, rotates it by the roll (`FreeCamMotion.RollLook`), and
  writes it back within the game's pitch limits. Unrolled, nothing is written.
  Confirmed in game 2026-09-21.
- **Looping over the top is not supported.** The game clamps pitch (about +45°
  up, −85° down) and the free-cam aims through its angles. Owning the
  orientation would allow loops but changes how flying feels and how tracks
  store aim; the user declined it on 2026-09-21 until there is a use case. Not
  in `FEATURES.md`.
