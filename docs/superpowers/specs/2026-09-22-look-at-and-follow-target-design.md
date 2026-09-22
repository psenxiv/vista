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

- A track under Follow Target names one character: a player by name and home world, or an NPC by
  name. It starts with none.
- **Duplicate names (decided):** among characters matching that name (and world, for a player),
  the one nearest the track's anchor is followed.
- **Aim height:** per track, in yalms above the character's feet, from 0 to 3. The default is
  1.3, about chest height.
- **Smoothing:** per track, from 0 (exact) to 1 (heavy). The default is 0.3. The aim point closes
  on the character with a time constant of Smoothing × 0.5 seconds, so at 1 it takes about half a
  second to catch up.
  - Smoothing starts afresh, with no easing, whenever playback starts, cuts to the entry, is
    seeked or scrubbed, or a preview starts.

**The Follow Target dialog**

- A floating window titled "Follow Target" holds the settings, so the track row keeps its fixed
  layout. It stays open while scrubbing and previewing, so the shot can be checked while tuning.
- In the aim menu, the Follow Target entry has a pencil icon at its right end (tooltip "Edit Follow
  Target"). Clicking the pencil opens the dialog, switching the track to Follow Target if it isn't
  already. Choosing the entry itself switches to Follow Target and opens the dialog when no
  character has been chosen yet. It closes with its Done button or its close button, and
  when the edited track leaves Follow Target or another track is edited.
- It holds, top to bottom:
  - a search box, focused when the dialog opens, filtering by any part of the name, ignoring case;
  - the characters loaded nearby, players and NPCs, sorted by name, each shown as
    "Name · World", or "Name · NPC" for an NPC. The one followed is highlighted. Picking one
    follows it and leaves the dialog open;
  - Aim height and Smoothing, each with a tooltip;
  - Done.
- Every change in it is one undo step, and it is disabled unless editing.

**The aim icon under Follow Target**

- The aim icon is drawn in the accent colour while the track follows a character that is found,
  and in red while it follows one that isn't, or none is chosen.
- Its tooltip names the state: "Follow Target: Name", or "Name (Not found): using recorded aim",
  or "Follow Target: choose a character".
- Look At and the other modes keep the plain icon.

**When the character can't be found**

- When no matching character is loaded, or none has been chosen, the camera uses the track's
  recorded aim, and the aim icon turns red as above.
- **Playlist (decided):** an entry whose track follows a named character that can't be found shows
  a warning icon in its row, with the tooltip "Not found nearby: using recorded aim", so the
  operator sees it before cutting to it.
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
  - `TargetName`, a string or null, and `TargetWorld`, a player's home world or null for an NPC;
  - `AimHeight`, a float;
  - `Smoothing`, a float.
- A new target source interface, `IAimTargets`, in Core: `Vector3? Find(string name, string? world,
  Vector3 near)`. Core never reads the game; the Plugin supplies it. `near` is the track anchor, used to
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
- `TrackEditorWindow` gains the aim modes, the menu's pencil, and the aim icon's colour and tooltip; a
  `FollowTargetWindow` holds the character list, aim height and smoothing.
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
