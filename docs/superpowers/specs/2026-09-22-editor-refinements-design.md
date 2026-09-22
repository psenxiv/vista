# Vista — Editor Refinements (Phase 3.e.2)

Date: 2026-09-22
Status: Approved

Follow-ups from the first look at 3.e.1: one layout for the Point window, clearer field labels,
tighter wording, a few moves in the main window, and track names in the world. Builds on
`2026-09-22-ui-polish-design.md` (3.e.1). This document wins where they differ.

Out of scope: the manual behind a `?` icon, which gets its own spec later.

## Point window

**One layout for points and anchors**

- An anchor (scene or track) uses the same layout as a point: the header row (Gizmo Move/Rotate,
  copy, paste, delete), then the position row, the rotation row and the FoV row.
- For an anchor, the fields it doesn't have are disabled and show "—": Pitch, Roll and FoV. Copy,
  paste and delete are disabled too.
- The window's title still says "Scene anchor" or "Track anchor".

**Fields**

- The text labels beside the fields (X, Y, Z, Pitch, Yaw, Roll, FoV) go.
- Each field has a 1-pixel border in its colour: X, Pitch in the X axis colour; Y, Yaw in the Y
  axis colour; Z, Roll in the Z axis colour, as the gizmo draws them. FoV keeps the default
  border.
- Each field's tooltip names it: "X", "Y", "Z", "Pitch", "Yaw", "Roll", "FoV". Tooltips show
  while disabled.
- Each row ends with an icon saying what the row is: a move icon for position, a rotate icon for
  rotation (tooltips "Position" and "Rotation"). The FoV row has no icon.
- The rows keep their order: X Y Z, then Pitch Yaw Roll (each under its axis), then FoV under X.

## Main window

- **Speed and Duration tooltips:** "Track speed" and "Track duration".
- **Add point tooltip:** "Add point".
- **LIVE** moves to the right end of the top bar, just left of the Hide UI eye.
- **Hide UI tooltip:** "Hide game UI when Live".
- **Point list:** the grip icon at the start of each row goes. Rows still drag to reorder.
- **Opening the window** sets it to its minimum width, every time it opens. Height is kept.

## Scene panel

- Each track row: the name on the left, then right-aligned the anchor button and the eye, with the
  eye rightmost.

## Playlist

- The loop cell's tooltip reads "Repeats".

## Tracks

- A new track's speed is 5 yalms per second (was 2).

## Track names in the world

- While editing, each track with a placed anchor shows its name just above its anchor ring, centred.
- It uses the anchor's colour: the edited track's in the anchor colour, the others dimmed as
  their rings are. A selected track anchor's name uses the selection colour.
- Hidden tracks show no name. Names hide with the rest of the overlay during a preview.

## Architecture

**Core**

- `TrackEditing.DefaultSpeed` becomes 5.

**Plugin**

- `PointWindow` draws points and anchors through one layout, with bordered, tooltipped fields and
  row icons.
- `TrackEditorWindow`: the tooltips, LIVE's place, the grip's removal, and minimum width on open.
- `HierarchyPanel` row layout; `PlaylistPanel` tooltip.
- `Overlay`/`EditorLayer` draw the track names.

## Testing

Core: the default speed test follows the new value. Everything else is checked in game with a
separate `CHECKLIST-3e2.md`.
