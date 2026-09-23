# Feature backlog

Ideas deliberately cut from v1, with the reason.

Only features actually discussed and agreed as deferred belong here. This is not
a place to park speculative ideas.

## Deferred from v1

On 2026-09-21 the user set phase 3, ahead of the switchboard. 3.a, Reverse and Ping-pong
playback, was done on 2026-09-22. On the same day the rest of phase 3 was reshaped around
Scenes, Tracks, anchors, a Playlist, saving and presets, each specced as it came up. Export/import
was dropped: sharing a Scene means sharing its file.

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

### Cutting to music
Load an audio track, show its waveform under a playlist-wide scrubber, drop markers on it,
and snap cuts (and later individual keys) to those markers. Users cutting a video to a
soundtrack could then line up their shots in Vista instead of eyeballing them and fixing it
in their video editor after the footage is shot. If Vista plays the audio, OBS captures it
alongside the footage, so lining the two up afterwards is a waveform match.

**Why not specced:** raised on 2026-09-23 as an idea only.

**Known so far:**
- Nothing in the stack plays audio files. Dalamud has no audio library, the game's
  `PlaySoundEffect` only plays its built-in effects, and .NET has no cross-platform
  playback. It would mean bundling NAudio. Dalamud's `ImGuiFileDialog` covers picking
  the file.
- Decode with fully managed code so Wine and Windows behave the same: WAV through NAudio,
  MP3 through NLayer. Avoid decoders that lean on the OS (ACM, Media Foundation) or ship a
  native library (libsndfile). NAudio has no cross-platform AAC or M4A decoder.
- The first open question is the clock. The audio position would have to drive shot time,
  and whether NAudio's position readout is steady enough for that, on Wine and on Windows,
  needs a spike.
- Snapping a cut is the ripple edit one level up. A playlist entry refers to a track by id,
  so the fitted duration probably belongs on the entry rather than the track. Entry loops
  and a looping playlist give a marker no single time.
- Depends on saving (3.f), since markers and the audio reference would be saved with the
  scene. It also claims the playlist, as the switchboard does.

### Starting characters with the camera (Brio)
When Live starts, unfreeze chosen actors through Brio, so the camera and their animation begin
together on every take. Brio's actor speed could also give slow motion under a moving camera.

**Why not specced:** raised on 2026-09-24 for GPose video makers; not explored yet. Brio publishes
an IPC API (`BrioAPI_V2.cs` in its repo) with `Actor.Freeze`, `Actor.UnFreeze`, `Actor.SetSpeed`
and `Actor.GetAll`. Open questions: which actors take part and how they're chosen, what happens
without Brio installed, and whether unfreezing lines animations up exactly on every take.

### Frame guides
Toggleable overlays for framing a recorded shot: 9:16, 16:9 and 2.39:1 masks, and a rule-of-thirds
grid, for creators filming Shorts and TikToks.

**Why not specced:** raised on 2026-09-24; not explored yet. Open questions: which ratios, where the
toggle lives, and whether the masks follow the game window or a chosen output size.

### Shaping the path
Widen or tighten the curve through a point without adding points, as with tension or handles in
Unreal, Blender and After Effects. Today the path is a centripetal Catmull-Rom spline, shaped only
by where the points are.

**Why not specced:** raised on 2026-09-24; not explored yet. Open questions: a per-point tension
(Kochanek-Bartels) or full handles, how it's edited in the world, and how it interacts with look-ahead
and the timing, which measures distance along the path.
