# Feature backlog

Ideas deliberately cut from v1, with the reason.

Only features actually discussed and agreed as deferred belong here. This is not
a place to park speculative ideas.

See `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` for v1 scope.

## Deferred from v1

On 2026-09-21 the user set phase 3, ahead of the switchboard. 3.a, Reverse and Ping-pong
playback, was done on 2026-09-22. On the same day the rest of phase 3 was reshaped around
Scenes, Tracks, anchors, a Playlist, saving and presets; `BRAINSPLAT.md` holds those
requirements and the work order, and each step is specced as it comes up. Export/import was
dropped: sharing a Scene means sharing its file.

### LookAt aim
A track's camera stays aimed at one fixed point in the world while it moves, such
as a spot over a stage.

**Why deferred:** cut from v1 on 2026-09-21. It needs a target placed with the
gizmo, which arrives with the editor.

### Aim tracking a game entity
A track's camera stays aimed at a character or object as it moves.

**Why deferred:** not a high priority.

## Under consideration

Raised by the maintainer, not yet agreed as a feature.

### OBS integration
Control the switchboard from OBS, so camera cuts can follow the stream.
