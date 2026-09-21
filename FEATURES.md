# Feature backlog

Ideas deliberately cut from v1, with the reason.

Only features actually discussed and agreed as deferred belong here. This is not
a place to park speculative ideas.

See `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` for v1 scope.

## Deferred from v1

### Export / import of shows
Tracks, snap points and a switchboard layout, exported as one portable JSON file
so a director can build shots and hand them to operators to run.

**Why deferred:** the serialisable data model ships in v1 regardless, because
the plugin config needs it. What export adds on top is file pickers, import
validation, version migration and zone-mismatch handling, none of which help
testing. As long as `CinematicCam.Core` stays free of game types, adding this
later is a UI layer rather than a refactor.

**Cost when picked up:** small.

### Playlists and auto-advance
A finished track advances to the next slot automatically, so a planned sequence
of shots runs unattended.

**Why deferred:** v1 holds the final frame when a track ends, which is the safe
behaviour on air. Auto-advance needs sequence editing and a clear way to abort
mid-sequence without the camera continuing to move.

**Cost when picked up:** moderate.

### LookAt aim
A track's camera stays aimed at one fixed point in the world while it moves, such
as a spot over a stage.

**Why deferred:** cut from v1 on 2026-09-21. It needs a target placed with the
gizmo, which arrives with the editor.

## Under consideration

Raised by the maintainer, not yet agreed as a feature.

### OBS integration
Control the switchboard from OBS, so camera cuts can follow the stream.
