# Vista — Scenes and Tracks (Phase 3.b)

Date: 2026-09-22
Status: Approved

A scene holds several named tracks, one of which is edited at a time. The track editor
window becomes the main Vista window, with a Hierarchy compartment beside the track editor.
Requirements come from `BRAINSPLAT.md`; this document wins where they differ.

Out of scope, each in its own later step: anchors (3.c), Edit Preview (3.d), the Playlist
(3.e), saving, scene names and several scenes (3.f), and presets (3.g). In 3.b there is one
scene, held in memory only.

## Terms

- **Scene**: the tracks the user is working with, in Hierarchy order, and which are hidden.
- **Edited track**: the one track the editor, the Point window, the Timing window and the
  gizmo work on. "Editing Track" in `BRAINSPLAT.md`.

## Model

- `Track` gains an **Id** (stable, never shown) and a **Name** (display text, not unique).
  Both live on the track because a preset (3.g) saves a whole track, and the Playlist (3.e)
  refers to tracks by Id.
- A new track is named "Track N", where N is one more than the number of tracks in the scene.
- **Clear track** empties the edited track's points and timing but keeps its Id and Name.
- `Scene` holds its tracks in order and the set of hidden track Ids.
- A new scene holds one empty track, "Track 1".
- A scene always has at least one track.

## Scene edits

Each is one undo step.

| Edit | Result |
|---|---|
| Add | An empty track goes at the end and becomes the edited track |
| Rename | The track's Name changes; an empty name is refused |
| Duplicate | A copy goes right after the original, with a new Id and the name "\<name\> copy", and becomes the edited track |
| Delete | Refused on the last track. Deleting the edited track makes the track that takes its place in the list the edited track, or the new last track if it was last |
| Move | The track moves to a new place in the list |
| Hide / Show | The track is added to or removed from the hidden set |

The edited track is always shown: hiding it is refused, and its eye in the Hierarchy is
disabled. Clicking a hidden track's row shows it (an undo step, like any Show) and makes it
the edited track (not an undo step, like any switch).

## Session

- The session holds the scene and the edited track's Id. Every existing track edit applies to
  the edited track and writes it back into the scene.
- **Undo covers the whole scene.** A snapshot is the scene, the edited track and the selected
  point. Undoing a change made in another track makes that track the edited track again, so
  the user sees what was undone. Undo keeps its 100 steps.
- **Switching tracks** is not an undo step. It clears the point, key and leg selection, and
  the scrub head goes to 0.
- **Live** plays the edited track, as today, until the Playlist takes over in 3.e. Everything
  that changes the scene or the edited track is disabled while live, paused included.

## Main window

- The track editor window becomes the **Vista** window, with compartments side by side: the
  **Hierarchy** on the left and the **track editor** in the middle. The Playlist joins on the
  right in 3.e.
- An icon at the left of the top row shows or hides the Hierarchy. Showing it widens the
  window by the compartment's width, and hiding it narrows it again, so the track editor
  keeps its size. The window's minimum width follows the open compartments.
- The Hierarchy has a fixed width in 3.b.
- The Point and Timing windows stay separate and follow the edited track.

## Hierarchy

- A header reads "Scene". The scene gets a name in 3.f.
- One row per track: an eye toggle, then the name. The edited track's row is highlighted. A
  hidden track's eye is shown closed.
- **Click a row:** the track becomes the edited track, and the editor camera flies to its first
  point, as jumping to a point does now. A track with no points leaves the camera where it is.
  In 3.c this becomes the track's anchor.
- **Double-click the name:** rename in place. Superseded by `2026-09-22-ui-polish-design.md`
  (3.e.1): double-click flies to the track's first point; Rename is in the right-click menu only.
- **Right-click:** Rename, Duplicate, Delete. Delete is disabled on the last track.
- **Drag a row:** reorder, as point rows reorder today.
- **+ Track** under the list adds a track. Superseded by `2026-09-22-ui-polish-design.md`
  (3.e.1): a plus icon in the header adds a track.
- No new keys. Delete and Backspace still delete the selected point.
- Disabled while live.

## Overlay

- Every shown track draws as the edited track does today: path, camera wireframes and
  numbered markers, at the same sizes. Tracks other than the edited track draw in grey.
- The edited track draws last, so it is on top.
- The gizmo appears only on the edited track's selected point.
- **Clicking a marker:** where markers overlap, the edited track's wins. Clicking another
  track's marker makes that track the edited track and selects that point, without moving the
  camera.
- Hidden tracks do not draw and cannot be clicked.

## Architecture

**Core**

- `Track` gains `Id` and `Name`; `TrackEditing.Empty` takes them.
- `Scene` record and `SceneEditing`, pure functions for the scene edits above.
- `SessionState` holds the scene and the edited track's Id; `Track` means the edited track.
  `EditSnapshot` becomes the scene, the edited track's Id and the selected point.
- Click hit-testing across tracks, with the edited track first.

**Plugin**

- `TrackEditorWindow` becomes the Vista window with the Hierarchy compartment.
- The overlay draws every shown track, caching each track's path by track.
- The editor's click handling switches tracks.

## Testing

Core under TDD:

- each scene edit, including the refusals (last track, empty name, hiding the edited track);
- new track names and duplicate names;
- Clear track keeping the Id and Name;
- switching tracks clearing the selection and the scrub head;
- undo and redo across tracks, including switching back to the track a change was made in;
- hiding and showing, and a hidden edited track being shown;
- click priority between overlapping markers of different tracks.

Then one `CHECKLIST.md` in the repo root, untracked, for the user to work through in game.
