# Feature ideas

Ideas not yet specced.

### Switchboard action buttons
A row of buttons, each tied to a track or a playlist, plus Cue and Cut. In Live, click a button, press Cue to line it up next, then Cut to swap the camera to it. How it behaves should follow hardware switchers such as the Blackmagic ATEM.

### OBS integration
Control the switchboard from OBS, so camera cuts can follow the stream.

### Cutting to music
Load an audio track, show its waveform under a playlist-wide scrubber, and snap cuts (and later individual keys) to markers dropped on it. Users cutting a video to a soundtrack could line up shots in Vista instead of fixing them in their video editor afterwards. If Vista plays the audio, OBS records it with the footage.

### Starting characters with the camera
When Live starts, unfreeze chosen actors through Brio, so the camera and their animation begin together on every take. Brio's actor speed could also give slow motion under a moving camera.

### Frame guides
Toggleable overlays for framing a recorded shot: 9:16, 16:9 and 2.39:1 masks, and a rule-of-thirds grid. Aimed at creators filming Shorts and TikToks.

### Shaping the path
Widen or tighten the curve through a point without adding points, as with tension or handles in Unreal, Blender and After Effects. Today the path is a Catmull-Rom spline, shaped only by where the points are.

### Filtering the Hierarchy
A search box over the Hierarchy's track list, for scenes with many tracks. It would filter by name as you type. No sort, since the Hierarchy's order is the user's to set by dragging.

### Save compatibility
Keep scene files backwards and forwards compatible now that saving is live: versioned files, and a migration that upgrades older saves to the current format when the schema changes. `SceneJson` reads only its own format version today, so there's no migration path yet.
