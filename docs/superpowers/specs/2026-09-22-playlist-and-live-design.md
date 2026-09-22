# Vista — Playlist and Live (Phase 3.e)

Date: 2026-09-22
Status: Approved

The scene gains a playlist: an ordered list of entries that each play a track. Live plays the
playlist instead of the edited track. Snap points become single-point tracks. Builds on
`2026-09-22-edit-preview-design.md` (3.d); requirements come from `BRAINSPLAT.md`. This
document wins where they differ, including over the main spec's Live and snap point sections.

Out of scope: a Next control and cutting by hand (the switchboard), transitions other than a
cut, more than one playlist, a looping playlist, and a slimmer Live layout.

## Model

- A scene has one **playlist**: an ordered list of **entries**.
- An entry has its own Id, the Id of the track it plays, an optional **loop count**, and a
  **transition** into the next entry. The only transition is Cut.
- A track can appear in several entries. A track need not appear at all.
- Duplicating a track doesn't add it to the playlist.

## Playlist edits

Each is one undo step, like any scene edit, and refused unless editing.

| Edit | Result |
|---|---|
| Add | An entry for a track goes in at a given position (or at the end), with no loop count |
| Remove | The entry goes |
| Move | The entry moves to a new position |
| Set loop count | Empty, or a whole number from 1 to 99 |
| Delete a track | Every entry for it goes too, in the same undo step |

## Loop count

- **Empty:** the entry follows its track. A track that doesn't loop plays once; a looping track
  holds the playlist on that entry for as long as Live runs.
- **N:** the entry plays its track N times, then moves on, whatever the track's own Loop
  setting. For Ping-pong, one loop is one round trip.

## Live

- Choosing Live cues the playlist paused at the first frame of its first playable entry. Play
  starts it.
- When an entry finishes, playback cuts to the first frame of the next playable entry. Time left
  over from the frame that finished carries into the next entry, so the timing doesn't drift; a
  long frame can pass through several short entries.
- After the last entry, the camera holds its final frame and stays live. Play then starts the
  playlist again from its first playable entry.
- An entry whose track has no points is skipped. When no entry can play, Live is refused and
  greyed out in the mode drop-down, with a tooltip saying to add tracks to the playlist.
- The scrub bar and seeking work within the entry that's playing, from 0 to its track's length,
  and stay within the loop pass it is on; an entry's completed loops still count.
  Restart goes back to the first playable entry.
- Play from Off goes live with the playlist, as it went live with the edited track before.
  This replaces 3.d's "choosing Live cues the edited track".
- Pause, resume and the scrub bar otherwise behave as in Live today.
- Nothing can be edited while live, the playlist included.

## Snap points

- `SnapPoint` and the Director's snap shot go. A snap point is a single-point track.
- A single-point track lasts as long as its point's hold. With a loop count N it plays that hold
  N times; with Loop on and no count it holds the playlist, which is how a snap point that stays
  on screen until the operator moves on is made.
- A single-point track always uses its recorded aim, since it has no direction of travel.
- A single-point track with no hold and no loop count shows one frame, then cuts on.

## UI

**Playlist compartment**

- On the right of the Vista window, with a show/hide icon in the top row beside the Hierarchy's.
  Showing it widens the window by its width and hiding it narrows it, as the Hierarchy does.
- One row per entry: its number, the track's name, its loop cell, and a remove button.
- **Loop cell:** click to edit. Empty means follow the track. Shown as "×N" for a count, and "∞"
  when a looping track with no count holds the playlist, both in an amber tint; nothing for an
  entry that plays once.
- Rows after an entry that holds the playlist are greyed out, since Live never reaches them.
- Drag rows to reorder. **+ Add** under the list opens a picker of the scene's tracks and
  appends the one picked.

**Hierarchy**

- Drag a track's row into the Playlist to add an entry at the drop position.
- A track's right-click menu gains **Add to playlist**, which appends an entry.

**In Live**

- The Playlist shows "2 / 3 — Crane" (the entry playing, the entry count, the track's name)
  and highlights the entry playing.
- Everything that edits is disabled, as today. The rest of the window keeps its layout.

## Architecture

**Core**

- `Playlist` and `PlaylistEntry` records and a `Transition` enum; `Scene` gains its playlist.
- Pure playlist edits, and track delete removing its entries.
- A playlist player that plays entries in turn: loop counts, skipping, carrying time over,
  holding at the end, seeking within an entry, and restarting.
- `Director` plays the playlist; the snap shot and `SnapPoint` go.
- `SessionState` goes live with the playlist, refuses Live when nothing can play, and exposes the
  entry playing.
- Single-point tracks use recorded aim.

**Plugin**

- The Playlist compartment, the Hierarchy's drag and menu item, and the mode drop-down's Live
  state.

## Testing

Core under TDD:

- each playlist edit and its undo, and track delete removing its entries;
- loop counts: empty following the track, N times, Ping-pong round trips, holding the playlist;
- advancing between entries with time carried over;
- skipping entries with no points, and refusing Live when nothing can play;
- holding the final frame at the end, and Play starting again;
- cueing at the first playable entry, seeking within an entry, Restart;
- single-point tracks as snap points, including recorded aim;
- the Director without the snap shot.

Then a separate `CHECKLIST-3e.md` for the user.
