# Vista — camera and gizmo editing (Phase 4.4)

Date: 2026-09-23
Status: Approved

Four changes to how the camera and the gizmo are driven: a gizmo space you can choose, direct
numeric access to the free camera, and two reset affordances.

## Gizmo space

`PointWindow` currently draws the text `"Gizmo"` and two framed radio buttons, Move and Rotate.
It becomes three frameless `IconButton`s, matching every other control in the window:

| | icon | tooltip | space |
|---|---|---|---|
| Move, world | `Globe` | Move (world) | `ImGuizmoMode.World` |
| Move, local | `Cube` | Move (local) | `ImGuizmoMode.Local` |
| Rotate | `SyncAlt` | Rotate | `ImGuizmoMode.Local`, unchanged |

The two Move buttons sit together; Rotate follows after a wider gap, because rotate has no
world/local choice to offer — it is already local-only — and the spacing should say so.

- `GizmoMode` gains `MoveLocal`, giving `Move`, `MoveLocal`, `Rotate`.
- `PointGizmo.Manipulate` passes `ImGuizmoMode.Local` instead of `World` when the mode is
  `MoveLocal`. Nothing else about translation changes.
- **R switches operation, not space.** `Toggle()` goes from either Move to Rotate, and from
  Rotate back to whichever Move space was last used. Pressing R twice returns you where you
  started. A `lastMove` field holds that space.
- Rotate keeps its disabled state for selections that only move, such as anchors, and Move,
  world shows as active whenever Rotate is disabled — the behaviour
  `!rotates || gizmo.Mode == GizmoMode.Move` gives today.

## Free camera window

Two new icon buttons on the top row, to the left of the fly-speed feather, in this order:

1. **Level roll** (`RulerHorizontal`) — sets the free camera's roll to 0 immediately. No window.
2. **Camera** (`Camera`) — toggles a **Camera** window.

Both appear only while editing, alongside the fly-speed control, because the free camera exists
only then. `FlySpeedStart` already computes where the speed control begins by measuring back from
the track row's trash icon; it subtracts the two new buttons so the whole group stays put.

The Camera window edits the free camera live, with the same field widget the Point window uses:

| field | reads | writes |
|---|---|---|
| X, Y, Z | free camera position | free camera position |
| Yaw, Pitch | `CameraAccess.ReadAngles` | `CameraAccess.WriteAngles` |
| Roll | `FreeCam.Roll` | `FreeCam.Roll` |
| Field of view | `FreeCam` fov | `FreeCam` fov |

`FreeCam` today only takes these through `Enable`. It gains setters for position, roll and fov.
Pitch is clamped through `EditLimits.Pitch` and field of view through `EditLimits.Fov`, so the
window cannot drive the camera somewhere the rest of the editor rejects.

This is not an undo step. The free camera is not scene state — flying somewhere has never been
undoable, and typing a coordinate is the same act.

## Reset field of view

The Point window gains a small `History` button beside the field-of-view field that writes the
game camera's **current** field of view into the selected point. This goes through the same
live-edit path as dragging the field, so it is one undo step.

Read at the moment it is pressed, rather than from a stored default: Vista has no field-of-view
constant, and the game's own value is what "back to normal" means.

## Not doing

- No world/local choice for Rotate. It has none today and the request was for translate.
- No field-of-view control on the Camera window's reset — roll is the only reset there, as asked.
- No rebinding, and no persistence of the chosen gizmo space across sessions.

## Testing

Core gets nothing new. `GizmoMode` lives in `Vista.Core.Editing` but has no Core or test caller
— all eight of its uses are plugin-side — so gaining a value needs no Core cover, and the clamps
this leans on (`EditLimits.Pitch`, `EditLimits.Fov`) are already covered.

Everything here is `Vista.Plugin` and goes on the Phase 4 in-game checklist: each of the three
gizmo buttons moving and rotating a point correctly; R returning to the Move space you left;
Rotate still greyed for an anchor; each Camera window field moving the camera and surviving a
drag; Level roll; and Reset field of view undoing in one step.

## Revision: one shared pose grid

Revised the same day, after the first in-game look.

- The Point and Camera windows draw one layout, `PoseGrid`: the gizmo and clipboard header, then
  position, rotation and field-of-view rows. The row icons moved from the right end of each row
  to a column on the left, inset like an icon button's glyph so they sit under Move (world).
- The ✥ and ⟳ icons stay labels in the Point window. In the Camera window ⟳ is a button that
  levels roll. The field-of-view row leads with a reset button in both: in the Point window it
  takes the camera's field of view, as before, and in the Camera window it takes the game's
  field of view from just before Vista took the camera (`CameraSession.TakeoverFov`, read from
  the takeover snapshot). It is shown disabled for anchors and Look At points, which have no
  field of view, and in the Camera window when Vista does not hold the camera.
- The Camera window shows the header row with every button disabled, so the windows match.
- The top-bar Level roll button stays alongside the Camera window's ⟳.
- The grid is drawn with `NoHostExtendX`. Without it the table stretched to the window's width,
  and because the header right-aligns to the table and the window sizes itself to its content,
  the width latched: a narrower anchor inherited whatever width a point had last made it. Copy,
  paste and delete now line up with the right edge of the Z field in every context.
