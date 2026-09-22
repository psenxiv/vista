# Vista — Look At and Follow Target (Phase 3.e.3)

Date: 2026-09-22
Status: Approved

Two new aim modes for a track: **Look At**, which keeps the camera on a fixed point in the
world, and **Follow Target**, which keeps it on a nearby character. Moves both out of
`FEATURES.md`. Builds on `2026-09-22-editor-refinements-design.md` (3.e.2) and
`2026-09-22-anchors-design.md` (3.c). This document wins where they differ, including over 3.e's
rule that a single-point track always uses its recorded aim.

Out of scope: aiming at an object that isn't a character, following more than one character, and
blending between aim modes.

## Aim modes

A track's Aim menu offers four modes:

| Mode | Where the camera looks |
|---|---|
| Recorded aim | The yaw and pitch recorded at each point, as today |
| Direction of travel | Along the path, as today |
| **Look At** | At the track's Look At point, all the way |
| **Follow Target** | At the track's character, at its aim height, all the way |

In every mode, roll and FoV still come from the points.

**Single-point tracks** use their recorded aim under Recorded aim and Direction of travel, as in
3.e. Under Look At and Follow Target they aim at the point or character, so a single point
becomes a fixed camera that pans to follow.

**Recorded yaw and pitch fields**

- **Look At:** disabled, as under Direction of travel.
- **Follow Target:** stay editable, because they are the aim used when the character can't be
  found.

**Aiming**

- The camera's yaw and pitch point from the camera to the target. Pitch is clamped as today, so
  the camera never turns straight up or down.
- A target closer than 0.1 yalms to the camera keeps the last good aim.

## Look At

- A track has one Look At point, which it keeps whichever aim mode is chosen. It is stored
  relative to the track's anchor, so moving or turning an anchor carries it, as it carries the
  points. Alt-moving the track anchor alone leaves it where it is in the world, as it leaves the
  points.
- **Placing it (decided):** the first time a track is set to Look At, its point is placed 10
  yalms along the first point's recorded aim. With no points, it goes 10 yalms ahead of the
  camera. Choosing another mode and coming back keeps it where it was.
- **Editing:** while the track is edited, the point is drawn in the world as a crosshair in the
  anchor colour, with a faint line to the camera path's first point. Clicking selects it. The
  gizmo moves it, with Move only, since rotation means nothing for it.
- **Point window:** a selected Look At point uses the Point window's layout, titled "Look At
  point". X, Y and Z work. The rotation row and FoV show "—" and are disabled, and so are copy,
  paste and delete, as for anchors.
- Every change to the point is one undo step. Delete and Backspace do nothing with it selected.
- Other tracks' Look At points are drawn greyed, as their anchors are, and only while their track
  is set to Look At. Clicking one makes that track the edited one, as clicking its anchor does.

## Follow Target

**The character**

- A track under Follow Target names one character, saved by name. It starts with none.
- **Choosing one:** a character button appears in the track row beside the Aim icon. It opens a
  list of the characters loaded nearby, players and NPCs, sorted by name, each with its distance
  from the camera. A search box at the top, focused when the list opens, filters it by any part of
  the name, ignoring case. Picking one saves its name.
- **Duplicate names (decided):** the character followed is the one with that name nearest to the
  track's anchor.
- **Aim height:** per track, in yalms above the character's feet, from 0 to 3. The default is
  1.3, about chest height. It sits in the track row as a small field with a tooltip, shown only
  under Follow Target.
- **Smoothing:** per track, from 0 (exact) to 1 (heavy). The default is 0.3. It is a slider in
  the track row, shown only under Follow Target. The aim eases towards the character rather than
  snapping, so bobbing and animation don't shake the shot: the aim point closes on the character with a
  time constant of Smoothing × 0.5 seconds, so at 1 it takes about half a second to catch up.
  - Smoothing starts afresh, with no easing, whenever playback starts, cuts to the entry, is
    seeked or scrubbed, or a preview starts.

**When the character can't be found**

- When no character by that name is loaded, or none has been chosen, the camera uses the track's
  recorded aim.
- The character button then shows the name followed by "(Not found)", with a warning icon, in
  red. Its tooltip reads "Not found nearby: using recorded aim". With none chosen it reads
  "Choose a character".
- **Playlist (decided):** an entry whose track follows a character that can't be found shows the
  same warning icon in its row, with the same tooltip, so the operator sees it before cutting to
  it.
- Once the character is found again, the aim eases back onto them.

**Editing and scrubbing**

- Scrubbing, previews and Live all aim at the character where they are now.
- **Marker (decided):** while the track is edited, the aim point on the character is drawn in the
  world as a small crosshair in the anchor colour. It can't be clicked or dragged.

## Undo

Choosing a mode, moving the Look At point, choosing a character, and setting the aim height or
smoothing are each one undo step, as any track edit is.

## Architecture

**Core**

- `AimMode` gains `LookAt` and `FollowTarget`.
- `Track` gains:
  - `LookAt`, a position relative to the track's anchor;
  - `LookAtPlaced`;
  - `TargetName`, a string or null;
  - `AimHeight`, a float;
  - `Smoothing`, a float.
- A new target source interface, `IAimTargets`, in Core: `Vector3? Find(string name, Vector3
  near)`. Core never reads the game; the Plugin supplies it. `near` is the track anchor, used to
  pick among duplicate names.
- `TrackEvaluator` takes an optional aim override: a world target position, or null for the
  recorded aim. It aims at that position when given, and uses recorded aim otherwise.
  Single-point tracks honour it.
- A small pure smoother, `AimSmoother`, eases a target position towards the live one by `dt`
  and `Smoothing`, and can be reset.
- `TrackPlayback`, `PlaylistPlayback` and the edit preview resolve each frame's target:
  - Look At: the track's world Look At point.
  - Follow Target: `IAimTargets.Find` plus the aim height, through the smoother.
  - Anything else, or not found: null.
  - They reset the smoother on start, cut and seek.
- `SceneGeometry`'s world view carries the Look At point through the anchors, like the points.
- `TrackEditing` gains the setters for the new fields. `SessionState` gains the matching edits,
  selection of the Look At point (`AnchorKind` gains `LookAt`, or a sibling selection kind), and
  live editing of it.

**Plugin**

- An `IAimTargets` implementation over Dalamud's object table: players and NPCs by name, nearest
  to `near`.
- A nearby-character list for the picker, sorted by distance to the camera.
- `TrackEditorWindow` gains the aim modes, the character button, aim height and smoothing.
- `PointWindow` handles the Look At point.
- `Overlay`/`EditorLayer` draw the Look At point and the character marker, and click the Look At
  point.
- `PlaylistPanel` gains the not-found warning.

## Testing

Core under TDD:

- aim from camera to target, pitch clamped, and the near-target guard;
- Look At: placement from the first point's aim and with no points, carried by anchor moves,
  kept across mode changes, and its undo;
- Follow Target: aiming at a found character plus aim height; recorded aim when not found or
  none chosen; duplicate names resolved nearest to the anchor;
- the smoother: easing by `dt`, 0 as exact, and resets on start, cut and seek;
- single-point tracks aiming under both modes, and keeping recorded aim under the other two.

Then a separate `CHECKLIST-3e3.md` for the user.
