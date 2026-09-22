# Vista — Anchors (Phase 3.c)

Date: 2026-09-22
Status: Approved design, awaiting spec review

The scene and each track get an anchor: a position and a yaw that everything attached to it
hangs off. Moving or turning an anchor carries its content, so a setup can be repositioned or
reused elsewhere. Builds on `2026-09-22-scenes-and-tracks-design.md` (3.b); requirements come
from `BRAINSPLAT.md`. This document wins where they differ.

Out of scope: saving (3.f), where 3.f's New scene places the scene anchor at the character's
feet; presets (3.g), which place a preset's anchor as a new track's would be placed.

## Terms

- **Anchor**: a position and a yaw. No pitch, roll or scale.
- **Scene anchor**: the scene's anchor, in the world.
- **Track anchor**: a track's anchor, relative to the scene anchor.
- **Local**: stored relative to an anchor. **World**: where it is in the game.

## Model

- A point is stored local to its track's anchor: its position is offset from the anchor and
  turned by the anchor's yaw, and its yaw is added to the anchor's. Pitch, roll and FoV are
  unchanged.
- A track anchor is stored local to the scene anchor, the same way.
- A point in the world is the scene anchor, then the track anchor, then the point.
- An anchor that has never been placed sits at the origin with yaw 0, which leaves its
  content where it is.

## Evaluation

Playback, the overlay, the Timing window and scrubbing work on the track converted to the
world. Moving and turning keep distances, so leg lengths, speeds and timing are identical
wherever the anchors are, and the timing code does not change.

## Editing in the world

Everything that edits a point in the world converts back to the track anchor: adding a point
from the camera (backtick and its variants, + Add), overwriting a point, the gizmo, the Point
window's fields, and pasting a point. The Point window shows world values, so a typed value is
where the point goes.

## Where anchors start

- **Scene anchor:** placed when the scene gets its first point, under that point at the
  character's foot height, yaw 0.
- **Track anchor:** placed when its track gets its first point, under that point at the
  character's foot height, yaw 0. On the scene's very first point both are placed, so that
  track's anchor starts at the scene anchor.
- **Duplicate** copies the anchor with the track, so the copy sits on the original until moved.
- **Clear track** keeps the anchor. A track's anchor is only placed under a first point once,
  when the track has never had one.
- If the character's position can't be read, the foot height is the point's own height.

## Moving anchors

- Moving an anchor carries what is attached: the scene anchor carries every track, a track
  anchor carries its points.
- **Holding Alt** moves only the anchor: what is attached stays where it is in the world, and
  its local values are recalculated.
- Each move is one undo step, whether a gizmo drag, a Point window edit or Bring scene to me.

## Drawing (Edit mode only)

- **Track anchor:** a ring flat on the ground with an arrow along its yaw, and a faint line to
  the track's first point. Grey for tracks other than the edited track, like their paths.
- **Scene anchor:** larger and in its own colour, a diamond with a yaw arrow.
- Hidden tracks' anchors do not draw and cannot be clicked.
- The edited track's anchor draws with the edited track, on top of other tracks.

## Selecting

- Clicking an anchor selects it. A point and an anchor are never selected at the same time.
- Clicking another track's anchor also makes that track the edited track.
- Click priority: the edited track's items first, as in 3.b; a point wins over an anchor where
  they overlap; the scene anchor comes after every track item.
- **Hierarchy:** each row gets an anchor button that selects that track's anchor (and makes it
  the edited track); the Scene header gets one that selects the scene anchor.
- Clicking empty space clears the selection, as today.

## The gizmo and the Point window on an anchor

- **Gizmo:** Move on all three axes; Rotate shows only the yaw ring. R switches between them,
  as today.
- **Point window:** X, Y, Z and Yaw for the selected anchor; no pitch, roll, FoV, copy, paste
  or delete. Typing moves the anchor and carries what is attached, like a plain drag.
- Delete and Backspace do nothing while an anchor is selected.

## Flying to a track

Clicking a Hierarchy row flies the editor camera to the track's anchor instead of its first
point: about 5 yalms behind the anchor along its yaw and 3 yalms up, looking at it. A track
whose anchor has never been placed leaves the camera where it is.

## Bring scene to me

A button on the Scene header. It moves the scene anchor to the editor camera's X and Z, at the
character's foot height, keeping its yaw and carrying every track. One undo step.

## Architecture

**Core**

- An `Anchor` record (position, yaw) with conversions between local and world for positions,
  yaws and control points.
- `Scene` and `Track` each gain an anchor and whether it has been placed, so placement on a
  first point happens once.
- A world view of a track for evaluation, drawing and scrubbing; `TrackEvaluator` itself is
  unchanged.
- Placement on the first point, carry and Alt moves, Bring scene to me, as pure functions.
- `SessionState` converts world edits to local, and gains anchor selection and anchor moves
  with undo.
- Click hit-testing gains anchors, with the priority above.

**Plugin**

- The character's foot height from the local player's position.
- The overlay draws anchors; the gizmo and the Point window handle an anchor selection; Alt
  is read during an anchor drag.
- The Hierarchy's anchor buttons, Bring scene to me, and flying to an anchor.

## Testing

Core under TDD:

- local to world and back for positions, yaws and control points, with nested anchors;
- placement on a track's and the scene's first point, including the foot-height fallback;
- a carried move and an Alt move, with nothing attached moving in the world on Alt;
- Duplicate copying the anchor, Clear keeping it;
- timing identical with anchors moved and turned;
- points added and edited in the world landing where they were put;
- anchor selection excluding point selection, and switching tracks from an anchor click;
- click priority between points, track anchors and the scene anchor;
- Bring scene to me;
- each anchor move being one undo step.

Then one `CHECKLIST.md` in the repo root, untracked, for the user to work through in game.
