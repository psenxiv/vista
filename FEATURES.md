# Feature backlog

Ideas deliberately cut from v1, with the reason.

Only features actually discussed and agreed as deferred belong here. This is not
a place to park speculative ideas.

See `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` for v1 scope.

## Deferred from v1

On 2026-09-21 the user set phase 3, ahead of the switchboard and split into sub-stages:
export/import, sessions holding multiple tracks, anchoring for tracks and sessions, playlists
with auto-advance, and two more playback modes: Reverse and Ping-pong. Looping either one comes
from the existing Loop mode. Details are settled after phase 2.

### Export / import
Tracks and snap points, exported as one portable JSON file so a director can
build shots and hand them to operators to run. Switchboard import/export is a
separate task, decided with the switchboard.

**Why deferred:** the serialisable data model ships in v1 regardless, because
the plugin config needs it. What export adds on top is file pickers, import
validation and version migration, none of which help
testing. As long as `Vista.Core` stays free of game types, adding this
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

### Aim tracking a game entity
A track's camera stays aimed at a character or object as it moves.

**Why deferred:** not a high priority.

### Anchored tracks
Every track hangs off a movable anchor instead of the world origin. Moving the anchor carries
all its points with it, so a track can be repositioned or reused elsewhere. A track remembers
the map it was made on, and importing it on a different map tells the user to adjust the anchor.

**Why deferred:** agreed on 2026-09-21 as the intended direction for tying tracks to a place,
but not yet scheduled. Until then tracks record no zone.

## Under consideration

Raised by the maintainer, not yet agreed as a feature.

### OBS integration
Control the switchboard from OBS, so camera cuts can follow the stream.
