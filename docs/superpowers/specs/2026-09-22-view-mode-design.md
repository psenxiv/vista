# Vista — View mode (Phase 3.e.3a)

Date: 2026-09-22
Status: Approved

The Off mode becomes **View**: the game keeps its camera, as Off does, and the scene stays drawn in
the world, view-only. Builds on `2026-09-22-look-at-and-follow-target-design.md` (3.e.3). This
document wins where they differ.

## Behaviour

- The mode drop-down reads View, Edit, Live. View replaces Off everywhere: in the drop-down, in
  logs, and in the code (`CameraMode.View`).
- In View the game has its camera and character exactly as Off did: nothing is locked or blocked.
- In View the overlay draws as it does in Edit: paths, points, anchors, track names, Look At
  crosshairs, and the followed character's marker, with the edited track highlighted and the others
  dimmed. Hidden tracks stay hidden.
- It is view-only: nothing can be clicked, selected or dragged, no gizmo shows, and no editor keys
  act. Nothing is shown as selected.
- Play from View still goes Live, as Play from Off did.
- Live and Edit previews still hide the overlay, as today.

## Architecture

- `CameraMode.Off` is renamed `CameraMode.View`.
- `EditorLayer` draws in View too, but skips clicks, gizmos and the selection highlight unless
  editing.

## Testing

The rename is carried by the existing tests. The overlay is checked in game with a line added to
`CHECKLIST-3e3.md`.
