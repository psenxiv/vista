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

### Manual
A manual for Vista, opened from a `?` icon in the window.

**Why deferred:** raised on 2026-09-22 during 3.e.2; to be specced on its own later.

## Under consideration

Raised by the maintainer, not yet agreed as a feature.

### Switchboard action buttons
A row of action buttons (name TBC), each registered to either a track or a playlist,
plus Cue and Cut. In Live, click an action button, press Cue to line it up next, then
Cut to swap the camera to it.

**Why not specced:** raised on 2026-09-23. Behaviour is to be settled after research
into how hardware switchers work, starting with the Blackmagic Design ATEM.

**Keeps `TrackShot` alive:** `Shot.cs`'s `TrackShot` has had no production caller
since the playlist became the only route to Live. It stays for the track-registered
action buttons. `GameCameraShot` has never had one at all.

### OBS integration
Control the switchboard from OBS, so camera cuts can follow the stream.
