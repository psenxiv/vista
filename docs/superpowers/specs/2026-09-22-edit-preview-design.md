# Vista — Edit Preview (Phase 3.d)

Date: 2026-09-22
Status: Approved design, awaiting spec review

Play in Edit mode previews the edited track in place instead of going live. Builds on
`2026-09-22-anchors-design.md` (3.c) and `2026-09-22-playback-direction-design.md` (3.a);
requirements come from `BRAINSPLAT.md`. This document wins where they differ.

Out of scope: the Playlist and Live playing it (3.e). Until then Live is unchanged.

## Starting a preview

- **Play in Edit mode** previews the edited track. The mode stays Edit, the game UI and
  Vista's windows stay visible, and Play/Pause shows Pause while it runs.
- It plays from the scrub head. If the scrub head is where the shot finishes (the end for
  Forward, 0 for Reverse and Ping-pong), it starts from the beginning instead.
- Direction and Loop apply as they do in Live.
- **Restart** is enabled in Edit mode and previews from the beginning.
- A track with no points can't be previewed, as it can't be played.

## While it plays

- The scrub bar and the Timing window's playhead follow the preview's shot time.
- The in-world drawings (paths, wireframes, markers, anchors and both gizmos) are hidden.
  They come back when the preview stops.

## Stopping a preview

A preview stops on any of these, and then the free-cam takes over from the frame shown and
the scrub head stays there, as a scrub release does (decided 2026-09-22):

- Pause;
- a track that doesn't loop reaching the end of its cycle;
- any flight key: movement, fly-down, roll, or the fly-speed wheel. Turning the camera with
  the mouse doesn't count;
- any edit to the scene or the edited track, including undo and redo, beginning a gizmo or
  field drag, and switching tracks;
- dragging the scrub bar, which then scrubs as usual.

Leaving Edit mode ends the preview. A preview never records an undo step.

## Live

Unchanged: choosing Live cues the edited track paused at its start, and Play there plays it
with the game UI hidden.

## Architecture

**Core**

- `SessionState` gains a preview: a `TrackPlayback` of the edited track in the world that
  runs only in Edit mode. While it runs, the scrub head reads its shot time.
- Play and Restart in Edit mode start it instead of going live; Pause stops it. Every edit
  path, undo and redo, switching tracks, beginning a live edit and beginning a scrub stop it.
- Starting from the scrub head seeks the preview there, restarting instead when that seek
  would finish it.

**Plugin**

- `CameraSession.Frame` advances the preview while editing, and when it stops hands the
  free-cam the last frame, as a scrub release does.
- The free-cam reports whether any flight key was pressed this frame; a press stops the
  preview.
- The editor layer skips the overlay, marker clicks and gizmos while a preview runs.
- The transport icons show Pause while previewing, and Restart is enabled in Edit mode.

## Testing

Core under TDD:

- where Play starts, including from the finishing position in each direction;
- Pause keeping the scrub head at the preview's shot time;
- a track that doesn't loop stopping at the end of its cycle, and a looping one continuing;
- each kind of edit, undo, switching tracks, a live edit and a scrub stopping the preview;
- Restart previewing from the beginning;
- leaving Edit mode ending the preview;
- a track with no points refused;
- Live unchanged.

Then a separate checklist file for the user.
