# Vista — UI Polish (Phase 3.e.1)

Date: 2026-09-22
Status: Approved

A cleaner Vista window with one visual style, plus the gaps found in the first look at 3.e:
anchors on the ground, a quieter track row click, a playlist loop, a Hide UI toggle and a LIVE
indicator. Builds on `2026-09-22-playlist-and-live-design.md` (3.e) and
`2026-09-22-anchors-design.md` (3.c). This document wins where they differ.

Out of scope: a custom-drawn timeline in place of the scrub slider, saving the Hide UI setting
(3.f), and toggling the playlist loop while live.

## Style

**Icon buttons**

- Every icon button is frameless: no background until hovered, then a soft rounded highlight.
- A toggle shows its icon in the accent colour when on and dimmed when off: the Hierarchy and
  Playlist toggles, Loop, the playlist loop, Hide UI, and a pinned point.
- Delete and remove buttons turn red on hover.
- Every tooltip still shows while the button is disabled.

**Row actions**

- A point's trash and an unpinned pin, and a playlist entry's remove button, show only while their
  row is hovered. A pinned pin always shows.

**Palette**

- One set of UI colours, defined once: accent (selection, toggles on), amber (loop counts), red
  (LIVE, destructive hover) and dim (off, greyed out).

**Headers**

- "Scene" and "Playlist" are muted text, with their controls right-aligned on the same line.

**Add buttons**

- Every "+ …" text button becomes a plus icon with a tooltip.

## Top bar

Left to right: Hierarchy toggle, Playlist toggle, the mode drop-down, LIVE, Undo, Redo, Timing,
fly speed, and Hide UI right-aligned.

- **LIVE:** red text that pulses slowly (about a 2-second cycle) while the mode is Live, cued or
  playing. Nothing is shown otherwise.
- **Fly speed:** a feather icon with a short slider showing the multiplier ("1x"); the tooltip
  reads "Fly speed". Shown only while editing, as today. It leaves the scrub row.
- **Hide UI:** an eye-slash toggle, off by default. While on, Live hides the game UI when it
  plays, as today. While off, the game UI stays up in Live. Edit previews never hide it.
  - It can be toggled in any mode. Turning it on while Live is playing hides the game UI at once;
    turning it off while Live has the game UI hidden shows it at once. A cued Live, not yet
    played, leaves the game UI up either way, as today.
  - It isn't an edit: no undo step. It isn't saved yet: it starts off each time Vista loads.
  - Tooltip: "Hide game UI in Live".
- **+ Add** leaves the top bar for the track row.

## Track row

Left to right: Aim, Direction, Loop, Speed, Duration, + Add, and Clear right-aligned.

- **Aim:** a crosshairs icon that opens the aim menu (Recorded aim, Direction of travel). Tooltip:
  "Select aim", followed by the current choice.
- **Direction:** an icon showing the current choice: right arrow for Forward, left arrow for
  Reverse, left-right arrows for Ping-pong. It opens the direction menu. Tooltip: "Select
  direction", followed by the current choice.
- **Speed:** a gauge icon before the field, tooltip as today. The word "Speed" goes.
- **Duration:** a stopwatch icon before the field, which reads "5.0 s". The word "Duration" goes.
- **+ Add:** a plus icon that appends a point, with a small caret beside it opening the insert
  menu, as today.

## Scene panel

- **Header:** "Scene", then right-aligned the scene anchor button and a plus icon that adds a
  track (tooltip "Add track"). The footer "+ Track" goes.
- **Bring scene to me** goes. Drag the scene anchor instead.
- **Track rows:**
  - A single click makes the track the edited one. The camera doesn't move.
  - A double-click flies the camera to the track's first point, as double-clicking a point row
    does. A track with no points leaves the camera where it is.
  - Right-click opens the menu, which keeps Rename, Duplicate, Add to playlist and Delete. Rename
    is only there now; double-click no longer renames.

## Anchors on the ground

- A first point's anchors (the track's, and the scene's when not yet placed) go on the ground
  under the point: the first collision hit straight down from the point.
- When nothing is hit, they fall back to the character's foot height, then to the point's own
  height, as today.
- This applies to anchors placed from now on. Nothing else moves.

## Playlist

**Header**

- "Playlist", or while live "2 / 3 — Crane" as in 3.e. Right-aligned: the playlist loop toggle
  and a plus icon that opens the track picker (tooltip "Add to playlist"). The footer "+ Add"
  goes.

**Playlist loop**

- A repeat toggle, off by default, tooltip "Loop playlist".
- While on, when the last entry finishes, playback cuts to the first frame of the first playable
  entry, carrying leftover time as between any two entries. Restart, cueing and Play behave as
  in 3.e.
- An entry that holds the playlist still holds it, so the loop never comes round.
- One Advance wraps at most once. A playlist whose every playable entry has no length shows one
  frame per entry per Advance, as 3.e does, and never spins.
- It belongs to the scene: toggling it is one undo step, and refused unless editing.

**Loop cell**

- Shows the count as a plain number, for example "4", in amber; "∞" in amber when the entry
  holds the playlist; "—" dimmed when it plays once.
- Tooltip: "Repeat Count".
- A single click turns it into a number box with the cursor in it. Enter or clicking away sets
  the count; Escape cancels. An empty box or 0 means follow the track. Numbers are clamped to 1
  to 99.
- The mouse wheel over the cell steps the count up or down by one, without scrolling the list.
  Stepping down from 1 empties it; stepping up from empty gives 1. Each step is one undo step.
- Dragging goes.

## Architecture

**Core**

- `Scene` gains `PlaylistLoops` (bool, default false) and a `PlaylistEditing` edit to set it;
  `SameValues` compares it.
- `PlaylistPlayback` takes a loop flag and wraps from the last entry to the first, at most once
  per Advance. `PlaylistShot` carries the flag; `SessionState.GoLive` passes the scene's.
- `SessionState` replaces its foot-height reader with a ground reader taking the point's world
  position, falling back to the point's height when it returns null. `BringScene` goes.
- `SessionState` gains `SetPlaylistLoops`.

**Plugin**

- A ground reader: a collision ray straight down from the point, falling back to the
  character's feet.
- `IconButton` restyled frameless, with a toggle and a hover-only variant; a UI palette.
- `CameraSession` gains the Hide UI setting, applied when Live plays and when toggled while
  playing; `BringSceneToMe` goes; a track's first-point fly replaces `OpenTrack`'s anchor fly.
- `TrackEditorWindow`, `HierarchyPanel` and `PlaylistPanel` rearranged as above.

## Testing

Core under TDD:

- the playlist loop: wrapping to the first playable entry with time carried; off still holding at
  the end; an entry that holds the playlist still holding; at most one wrap per Advance; the
  toggle as one undo step and refused unless editing;
- anchors placed at the ground reader's height, and at the point's height when it returns null;
- `BringScene` removed with its tests.

The UI changes are checked in game with a separate `CHECKLIST-3e1.md`.
