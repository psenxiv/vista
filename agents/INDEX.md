# Index

Where Vista's code lives, and which classes own the general-purpose logic. Check the helper homes before writing a helper.

## Projects

- `src/Vista.Core`: the logic, with no Dalamud, no game types and no `unsafe`. Anything that can be decided and tested without the game goes here.
- `src/Vista.Plugin`: Dalamud, hooks, game memory and ImGui. It draws, hooks and handles input, and holds as few decisions as it can.
- `tests/Vista.Tests`: the tests, which reference Core only. Folders mirror Core's.

## Packages

### Core

- `Camera`: the camera's pose (`CameraState`), angle and rotation maths, free-cam motion and fly speed, screen projection, and the rules every frame written must keep.
- `Display`: what the editor draws, as numbers: the camera glyph, track paths and turn heat, marker hit tests, the timing graph and its view, a view's zoom kept between frames, tick spacing, row text fitting, edge scrolling, panel widths, the track name size, and the scrub bar over a playing track or playlist.
- `Editing`: turning input into edits: pose field limits, gizmo matrices and drags, clicks on markers and list rows, block moves of dragged rows, wheel notches, and field values held until let go.
- `Guide`: reading the User Guide's Markdown pages and index into blocks and topics.
- `Input`: the key, modifier and hotkey types, the table naming every hotkey Vista binds, and which hotkey a press or held keys resolve to.
- `Scenes`: scenes, playlists, switchboards and presets as data, their edits and names, their place in the world, and their files.
- `SelfTest`: the rules that decide whether each `/vista selftest` check passed, and its report lines.
- `Session`: the mode, selection, undo history, the Edit previews (the edited track's and Edit's throwaway switchboard's) and scrub head, the switchboard player that cuts Live or Edit between slots and has the Director play the one on Program, and the scene's tracks in the world.
- `Tracks`: the track and its control points and anchor, editing its points and timing, and evaluating it at a moment.
- `Tracks/Aiming`: where the camera looks and which way is up: recorded aim (with roll and field of view, blended by distance along the path), direction of travel, Look At, watched and followed characters, and smoothing.
- `Tracks/Playback`: the Director, playing, pausing and scrubbing a track or a playlist frame by frame, what a scrub bar draws from (`IPlayingShot`), and the playlist laid end to end as Live's timeline.
- `Tracks/Spline`: the Catmull-Rom path and its arc-length table.
- `Tracks/Timing`: timing keys, compiling them from speeds and holds, the timing curve, and easing.

### Plugin

- `Editor`: what's drawn over the game in Edit and View: the overlay, the point and anchor gizmos, editor keys and colours.
- `Game`: reading and writing the game: the camera and its hook, the free-cam, input blocking, movement lock, the UI toggle, ground and nearby characters, and faults.
- `SelfTest`: running `/vista selftest` in the game.
- `Session`: carrying the session's mode changes out in the game, and the save folder and scene files.
- `Ui/Main`: the main Vista window, with its menu bar, its Hierarchy and Playlist panels, its point list, and the edited track's scrub bar.
- `Ui/Widgets`: the ImGui pieces the windows share.
- `Ui/Windows`: every other window: Camera, Point, Timing, Target, Watch and Follow Target, Switchboard (with the scrub bar over its Program shot), User Guide, Setup, Welcome and the scene, preset and playlist picker, with one source per kind it lists.
- `Guide` and `Demo` hold the User Guide's Markdown pages and the demo scene, embedded in the plugin.

## Helper homes

Put general logic in the home for its kind; add a home here when a new kind needs one.

### Angles, rotations and vectors

- `Camera/Angles`: the degree constant, degrees and radians, wrapping to half a turn, the shortest signed difference between two angles, and unwrapping a sequence of angles.
- `Camera/Vectors`: the angle between two vectors, the signed angle about an axis, normalising or flattening with a fallback, and whether a vector is finite.
- `Camera/CameraRotation`: rotations from yaw, pitch and roll and back, the facing at a yaw and pitch and the yaw and pitch of a facing, rotations from a forward and up and their basis matrix, the forward and up of a rotation, squaring an up to a forward, rolling an up about a forward, the upright up, and the shortest rotation between two directions.
- `Camera/CameraState`: a camera frame from a rotation or from angles, its forward and its roll.
- `Camera/FreeCamMotion`: a free-cam step, turn and roll, the look-at distance, and a look-at point from angles or a rotation.
- `Editing/PoseMatrix`: a pose to and from the gizmo's matrix, and a matrix's unit forward and up.

### Scalars and curves

- `Tracks/Fraction`: clamping to [0, 1], and how far a value lies along a span, with the answer for an empty span given by the caller.
- `Tracks/Timing/Hermite`: the cubic Hermite basis on numbers and on points in space, its slope, smoothstep, the Fritsch–Carlson clamp that keeps a cubic monotone, the three-point slope weights through a point and at an end, and a key's slope for the aim channels: the three-point slope limited so it doesn't overshoot, or the end leg's own rate at an end (on numbers and on turns).
- `Tracks/Aiming/AimSmoother`: the share of the way an eased value moves in a frame at a smoothing, and easing a position with it.
- `Tracks/Search`: the binary search over an ascending array that curves and tables use to find their interval.
- `Tracks/Spline/CatmullRom` and `ArcLengthTable`: the path through points, its derivative, and walking it by distance.

### Limits

- `Editing/EditLimits`: the range of each pose field (pitch, angle, FoV, coordinates): a value that isn't finite changes nothing, a finite one is clamped or wrapped; and an angle in degrees as the fields show it.
- `Tracks/TrackEditing`: each track setting's default and range (speed, seconds, aim height, look ahead, smoothing), applied by its setter; whether an index is a point, leg or key; whether a track has a point to play.

### Names

- `Scenes/SceneNames`: checking scene, preset, track and slot names, and suggesting the next free name or a copy's name.

### File listings

- `Scenes/FileEntry` and `FileList`: a scene or preset file as the picker lists it, and filtering a listing by name.
- `Scenes/UnreadableNotices`: the notice for a scene file that can't be read, given once per file; `SceneLibrary.Notice` says when opening skipped the last scene for it.

### List edits

- `Editing/ListEdit`: finding an item by a test, and a copy of a list with one item replaced or items inserted.
- `Editing/RowPicking`: a click on a list row, with Ctrl and Shift, and what a drag carries.
- `Editing/BlockMove`: the new order when rows are dragged as a block, and applying an order to a list.
- `Scenes/SceneEditing` and `PlaylistEditing`: finding a track, playlist or playlist entry by id, and refusing unknown ones with their "no such" message; the selected playlist; whether a playlist has an entry that can play; playlist names and the create, rename, duplicate, delete and select rules.
- `Scenes/SwitchboardEditing`: the slot count, the track or playlist a slot points at, the name a slot shows, whether a slot can play and the hint when it can't or is empty, assigning, renaming and clearing a slot, the toggles, emptying the slots on a deleted track or playlist, and keeping Program, Next and resume points in step with the slots.

### Formatting

- `Display/Units`: how seconds, yalms, speeds and degrees are shown, as ImGui field formats and as text, always with a full stop.
- `Display/Ticks`: tick spacing and the label format for it, on both axes of the timing graph.
- `Display/RowFit`: cutting a row's name to fit with an ellipsis, and scrolling it while hovered.

### Hit tests

- `Display/MarkerHitTest`: the item nearest a click within a radius, with ties to the earlier or the later; `TrackMarkerHitTest` ranks the kinds of track marker on it.

### Views

- `Display/ViewZoom`: a timing graph's or scrub bar's zoom kept between frames.

### Field edits

- `Editing/PendingEdit`: a field's changed value, held while the field is held and applied once it's let go, only if it changed and can still apply.

### Timing

- `Tracks/TrackEditing`: which timing key is a point or a hold end, and the keys a leg runs between.
- `Tracks/TrackEvaluator`: leg lengths and times, point times, distance and speed along the path at a time, and the look-ahead spot a distance further along it; `Tracks/EvaluatorCache` keeps one track's evaluator until the track changes.

### Playback clock

- `Tracks/Playback/PlaybackClock`: a cycle's length, wrapping a clock into its cycle, and mapping a playback clock to a shot time and back.
- `Tracks/Playback/PlaylistTimeline`: each playlist entry's start, length and passes, the total, and playlist time to and from an entry, pass and time.

### UI widgets

- `Ui/Widgets/Feedback`: the feedback form's link and label, and opening it.
- `Ui/Widgets/IconButton`: frameless icon buttons with tooltips, toggles, window toggles, row actions, row hover, icons drawn as text, the not-found warning, and icon and row widths.
- `Ui/Widgets/Layout`: the spacing and field and dialog widths the windows share, right-aligning, `CentreRemaining` to centre content in the space left in a window, `CentredText` to draw a line centred there, centring a window as it appears, `PadLikeWindowTop` to match the window's top padding, and minimum window sizes.
- `Ui/Widgets/WindowStyle`: a window's spacing, popup style and selected-row colours, or the popup style alone.
- `Ui/Widgets/ScrubBar`: what the scrub bars share: their smaller grab and zooming with the wheel.
- `Ui/Widgets/Tooltip` and `Menu`: a tooltip on the item just drawn, shown even while disabled, and a menu's items: plain, ticked, and a labelled slider.
- `Ui/Main/AddPointItems`: the three ways to add a point at the camera as menu items, shared by the Edit menu and the track row's add menu.
- `Ui/Widgets/PoseGrid`: the Point and Camera windows' shared layout and fields.
- `Ui/Widgets/BorderedField`, `PendingField`, `LiveDrag` and `TextEdit`: a bordered number field, a field drawn for a `PendingEdit`, a field previewed live as one undo step, and text edited in place; with `FieldDraw`, `PendingField` and `LiveDrag` run a field the caller draws.
- `Ui/Widgets/NameStrip`: the band under a side panel's heading that names its open scene or selected playlist.
- `Ui/Widgets/DragRows` and `RowText`: dragging list rows, their drag source, the space under a list and reading a row's click, and a row's fitted name, inside the item just drawn or an explicit rectangle.
- `Ui/Widgets/CharacterPicker` and `Refusal`: the character drop-down, and telling the player why an action was refused (the log and a notification).
- `Ui/Widgets/Notice`: a warning notification that fades, which `Refusal` and the scene files' unreadable-file notices show.
- `Ui/Widgets/PromptDialog`, `NamePrompt` and `DeleteConfirm`: a modal's begin and Cancel, a name prompt (Ok or Replace, checked against a caller-given refusal and notice), and a delete confirmation; each instance keeps its own popup id.
- `Ui/Widgets/PresetSave`: saving a track as a preset, its name prompt shared by the Hierarchy row menu and the Scene menu.

### Colours

- `Ui/Widgets/UiColours`: the Vista window's colours, selected rows included.
- `Editor/EditorColours`: the overlay's colours.

### Gizmos

- `Editor/Gizmo`: the ImGuizmo setup and calls the point and anchor gizmos share, and waiting out a drag one of them dropped.
- `Editor/Overlay`: the line thicknesses the overlay and the Timing window's key rings draw with.

### Play and scrub

- `Session/GameSession` (plugin): starting, pausing and restarting playback, and Play/Pause as one toggle.
- `Session/Scrubber` (plugin): a scrub one control began, which only that control ends.
- `Ui/Main/EditCommands` (plugin): Undo, Redo, Play/Pause and Restart from the toolbar and menus, applying the held field first, and the debug self-test guard.

### Files

- `Scenes/SceneFolder`: scene and preset files, backing up and upgrading older scene files, and which exceptions are the file errors callers expect.
- `Scenes/FormatVersions`: the settings' record of the format each kind of file was last upgraded to, and whether the scene upgrade pass is due.

### Game access

- `Game/CameraAccess`: reading and writing the world camera.
- `Game/CharacterTable` and `Ground`: the characters loaded nearby, and the ground under a point.
- `Game/PhysicalKeys`: keys read from their physical state.
- `Game/HotkeyKeys`: mapping a bound hotkey's Core key to Dalamud's `VirtualKey`, the only place that does, and reading the modifiers held.

### Test fixtures

- `Fixtures`: what's used across test areas: control points and tracks through them, Guard and the tracks watching them, going live, a scene with one playlist and the selected playlist's entries, a scene with two tracks and two playlists and one with slots on air, resume lists, switchboard and Live assertions, generators (points, path tracks, target settings, frame steps), finite-difference slopes, the counterexample printer, the well-formed frame assertion, a well-formed frame value, and vector assertions.
- `PathShapes`: the path shapes the movement sweep and the camera regression scene play, as positions, and the field of view their points are recorded at.
- `TrackRuns`: playing a track, or hand-made motion, and measuring it: a run's frame at a time, length and point arrive and depart times; clocks (fixed steps, and the evaluator, a playback or the Director stepped within a frame budget); and measures: steps and snaps, picture twist, the largest change or value over samples and when, world turn rate, speed, and well-formed on every frame.
- `Session/SessionFixtures`: the sessions the session tests start from, track and entry ids by index, and a control point at head height.
- `Tracks/Timing/TimingFixtures`: key times, key drags, and asserting a track's key times within tolerance.
- `Tracks/Playback/PlaybackFixtures`: the tracks and playlist items the playback tests play, and generated played tracks and playlist scenes.
- `Scenes/SceneFixtures` and `TempFolder`: scenes told apart by name, format 1 scene and preset files, and a scene folder in a temporary directory, which can block its backups.
