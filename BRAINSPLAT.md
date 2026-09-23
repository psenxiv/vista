# Vista — Scene / Track / Playlist Model

High-level requirements for phase 3. Each section is specced on its own, in the order under
Work order; a spec wins over this document where they differ. Decisions settled with the user
on 2026-09-22 are folded in below.

## Core hierarchy

Vista should use the following organisational hierarchy:

```text
Scene
 ├─ Track A
 │   └─ Control Points
 ├─ Track B
 │   └─ Control Points
 ├─ Track C
 │   └─ Control Points
 └─ Playlist
     └─ Ordered references to Tracks
```

The important distinction is:

- **Scenes** organise a complete camera setup for a location.
- **Tracks** represent individual camera shots.
- **Control Points** define the spatial path of a Track.
- **Playlists** determine which Tracks are played, and in what order, during Live mode.

Tracks exist independently of the Playlist. A Track does not need to be included in the Playlist.

---

## Scene

A **Scene** is the top-level authored camera setup.

Prefer the term `Scene` rather than `Session`. `Session` sounds temporary, whereas `Scene` better represents a durable camera setup associated with a particular place.

A Scene should contain conceptually:

```text
Scene
- Id
- Name
- Anchor Transform
- Tracks[]
- Playlist
```

A Scene is named when it is created, like a project in Unity. Scene names are unique, because
a Scene is saved as a file named after it.

The Scene anchor is relative to the current game world/map.

Moving or rotating the Scene anchor should transform every Track in the Scene, and therefore every Control Point within those Tracks.

Conceptually:

```text
World
  └─ Scene Transform
      └─ Track Transform
          └─ Control Point
```

A Control Point's world-space transform should therefore be derived from its local transforms rather than treated as an unrelated absolute value.

For example:

```text
WorldPoint =
    SceneTransform
    * TrackTransform
    * LocalControlPoint
```

Anchors carry a position and a yaw only: no pitch, roll or scale. Yaw is useful because an entire camera setup may need to be reused somewhere with a different orientation; tilting would tilt every horizon and push shots past the game's pitch limits.

---

## Track

A **Track** represents one camera shot.

Rather than treating properties such as leg durations, velocity, etc. as fundamental Track concepts, the Track should conceptually own:

```text
Track
- Id
- Name (a display string; not unique)
- Anchor Transform
- ControlPoints[]
- Aim Settings
- Motion / Timing Settings
- Playback Settings
```

The Track anchor is relative to the Scene anchor.

Moving or rotating a Track anchor should transform all Control Points within that Track without affecting other Tracks.

A Track's Control Points should therefore preferably be stored relative to the Track transform.

A new Track's anchor sits at its first point's horizontal position, at the height of the
character's feet. The character is locked in place standing on the floor while editing, so this
is usually floor level.

Snap points are not a separate concept: a snap point is a single-point Track with a hold, or
with Loop on. A single-point Track always uses its recorded aim, since it has no direction of
travel.

Conceptually, a Track is:

> A camera path plus the rules used to traverse it.

This leaves room for the timing model to evolve without baking the current leg-duration implementation into the core data model.

---

## Editing Track

There should only be one **Editing Track** at a time.

Prefer the term `Editing Track` or `Selected Track` rather than `Active Track`, because `Active` could be confused with whether a Track participates in playback.

The Editing Track is simply the Track currently loaded into the editing panel.

Behaviour:

- The Editing Track is rendered normally.
- Other Tracks remain visible in the world but are visually subdued / greyed out.
- Clicking a Control Point belonging to another Track automatically switches the Editing Track to that Track.
- Changing the Editing Track does not affect whether that Track is present in the Playlist.
- Editing state and Live playback state are separate concepts.

---

## Playlist

A **Playlist** is an ordered list of references to Tracks.

It determines which Tracks are played, and in what order, during Live mode.

Example:

```text
Scene Tracks:
- Establishing
- Orbit
- Closeup
- Crane
- Debug Test

Playlist:
1. Establishing
2. Crane
3. Closeup
```

`Orbit` and `Debug Test` still exist in the Scene; they simply are not included in Live playback.

Tracks should not contain an `Order` property for Live playback. Playback order belongs to the Playlist.

Looping belongs to the Playlist entry, not the Track, so the same Track can appear twice with
different counts:

- **No loop count set:** the entry follows the Track. A Track that plays once plays once; a
  looping Track holds the Playlist.
- **A count of N:** the entry plays N times, then advances, whatever the Track's own setting.
  For Ping-pong, one loop is one round trip.

The Playlist shows looping entries with the loop icon, or ∞ for an entry that holds the
Playlist, in a subtle tint. There is no Next control yet (that belongs to the switchboard), so
the entries after an entry that holds the Playlist are unreachable; the Playlist greys them out.

For now, a Scene should contain one Playlist. However, the Playlist should still be modelled as a distinct concept so that multiple Playlists could be supported later if useful.

Possible future examples:

```text
Main Show
Opening Sequence
Boss Intro
Screenshots
```

---

## Playlist transitions

For the first implementation, moving between two Tracks should behave as an instantaneous camera cut:

```text
Track A
  ↓
Cut
  ↓
Track B
```

The camera jumps from the final frame of Track A to the initial frame of Track B.

Even if only `Cut` is currently supported, the transition should conceptually exist as its own idea rather than being buried inside Track playback.

For example:

```text
PlaylistEntry
- TrackId
- TransitionToNext
```

Initially:

```text
TransitionToNext = Cut
```

This leaves room for future transition types such as:

```text
Cut
Blend 0.5s
Blend 1.0s
```

without needing to redesign the sequencing model.

---

## Edit playback vs Live playback

Playback in Edit mode and playback in Live mode should become explicitly different behaviours.

### Edit Preview

Pressing Play while in Edit mode should:

- play only the current Editing Track;
- keep the UI visible;
- remain in Edit mode;
- allow normal scrubbing, pausing and inspection;
- pause the preview when anything is edited;
- act as a preview / testing mode for the current shot.

Conceptually:

```text
Edit playback = local Track preview
```

Pressing Play in Edit mode should no longer automatically enter Live mode.

### Live Playback

Live mode should be entered explicitly.

Pressing Play while in Live mode should:

- play the Scene's Playlist;
- progress through Playlist Tracks in order;
- perform cuts between Tracks;
- hide the game UI as appropriate;
- behave as the presentation / production mode.

Conceptually:

```text
Live playback = Scene / Playlist playback
```

This distinction becomes increasingly important once Playlists, transitions and the future live switchboard exist.

---

## UI structure

Vista uses one main window with three compartments, each shown or hidden by buttons in its top
row: the Hierarchy on the left, the Track editor in the middle, and the Playlist on the right.
The Point and Timing windows stay separate. Fewer windows matter, because the game view is
already crowded.

- **Hierarchy:** Scenes and their Tracks, not their Control Points. Clicking a Track makes it
  the Editing Track and flies the editor camera (never the character) to its anchor.
- **Playlist:** edited in Edit mode; in Live mode the same compartment shows what is running.

Avoid making `Edit`, `Live`, `Scenes`, and `Playlist` four equal top-level tabs.

They represent different kinds of state:

- **Mode** answers: what is the camera currently doing?
- **Scene** answers: what authored camera setup am I working with?
- **Track** answers: which shot am I currently editing?
- **Playlist** answers: which shots will run, and in what order, during Live playback?

A cleaner high-level structure would be:

```text
Vista

Mode:  [ Edit ▼ ]
Scene: [ Rainveil Main Stage ▼ ]

[ Track ] [ Playlist ]
```

### Edit mode

The Track tab could show:

```text
Track: [ Crane Sweep ▼ ]

Aim: ...
Timing: ...
Points: ...
Scrubber: ...
```

The Playlist tab could show the ordered Live sequence without leaving Edit mode.

Scene management can remain lightweight through the Scene selector:

```text
New
Rename
Duplicate
Delete
```

A Scene is moved by dragging its scene anchor with the gizmo.

A dedicated Scene Manager is not necessary unless Scene management becomes substantially more complex later.

### Live mode

Switching to Live mode should simplify the UI substantially.

For example:

```text
Vista

Mode: Live
Scene: Rainveil Main Stage

Playlist
3 tracks • 24.6 s

[ Play ] [ Pause ] [ Restart ]

Now Playing:
2 / 3 — Crane Sweep
```

The Live UI should focus on execution rather than editing.

---

## Scene position

A Scene has no Territory / Map association. It is pinned to the world position it was saved
at. Loaded on another map, it appears at those coordinates, and the user repositions it by dragging the scene anchor.

---

## Saving

Scenes are saved as JSON files, one per Scene, named after the Scene. There is no export or
import: sharing a Scene means sharing its file.

The user picks a parent folder, and Vista keeps a `vistaxiv` folder inside it:

```text
<parent>/vistaxiv/
  scenes/    one JSON file per Scene
  presets/   one JSON file per preset Track
```

- The folder is chosen up front, with Dalamud's folder picker, before the plugin can be used.
  If it is ever unset or cannot be found, Vista returns to that choice.
- The default is the plugin's config folder.
- A Scene saves itself shortly after each change (debounced).
- Undo history clears when a Scene is loaded or switched.

---

## Presets

A preset is a Track saved on its own, with all its configuration and Control Points, in
`presets/`. Loading a preset adds it to the current Scene as a new Track. Its anchor is placed
as a new Track's would be, and its points follow, since they are stored relative to it.

Saving a Track as a preset sits on the Track's right-click menu in the Hierarchy. Adding one
sits in the Hierarchy's add menu (Empty track, or From preset).

---

## Recommended conceptual model

The resulting model is:

### Scene

A durable top-level camera setup for a location.

Owns:

- its name;
- Scene anchor;
- Tracks;
- Playlist.

### Track

One camera shot.

Owns:

- Track anchor;
- camera path / Control Points;
- aim configuration;
- motion / timing configuration;
- playback configuration.

### Editing Track

Transient editor state identifying which Track is currently being manipulated.

Only one Track may be the Editing Track at a time.

### Playlist

An ordered set of references to Tracks determining Live playback order.

Tracks can exist without being in the Playlist.

### Edit Preview

Plays only the Editing Track while remaining in Edit mode with the UI visible.

### Live Playback

Runs the Playlist as the production / presentation sequence.

### Transition

Defines how playback moves from one Playlist Track to the next.

Only `Cut` needs to exist initially.

### Preset

A Track saved on its own, added to any Scene as a new Track.

---

## Design principle

The core mental model should be:

```text
A Scene contains shots.
A Track is one shot.
Tracks can be edited independently.
A Playlist determines how shots are performed in Live mode.
```

This moves Vista away from being merely "one spline editor that can play a camera" and towards a small camera-shot system, while still avoiding the complexity of becoming a full nonlinear video or animation editor.

---

## Work order

Each step is specced, planned and built on its own, when it comes up. Data models come before
saving, so the file format is written once.

1. **3.b Scene and Tracks.** Named Tracks, one Editing Track, other Tracks greyed and
   clickable, undo across the Scene, and the main window rebuilt into compartments with the
   Hierarchy.
2. **3.c Anchors.** Scene and Track anchors (position and yaw), points stored relative to them,
   gizmo handles for anchors, default placement, and flying to an anchor
   from the Hierarchy.
3. **3.d Edit Preview.** Play in Edit mode previews the Editing Track; editing pauses it.
4. **3.e Playlist and Live.** Playlist entries (Track, loop count, Cut), the Playlist
   compartment, Live playing the Playlist, loop indicators, and snap points folded into
   single-point Tracks.
4a. **3.e.1–3.e.3 Polish and aim.** UI polish, editor refinements, and the Look At and Follow
   Target aim modes (moved up from the backlog on 2026-09-22).
4b. **3.e.4 First tester release.** Publish the unsaved build as an experimental plugin through a
   custom plugin repository, so testers can get a feel for it before saving exists.
5. **3.f Saving and loading.** The `vistaxiv` folder and its up-front choice, Scene files with
   debounced saving, and the Scene selector.
6. **3.g Presets.**

Then the switchboard.
