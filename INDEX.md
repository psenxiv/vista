# Index

Where Vista's code lives, and which classes own the general-purpose logic. Check the helper homes before writing a helper.

## Projects

- `src/Vista.Core`: the logic, with no Dalamud, no game types and no `unsafe`. Anything that can be decided and tested without the game goes here.
- `src/Vista.Plugin`: Dalamud, hooks, game memory and ImGui. It draws, hooks and handles input, and holds as few decisions as it can.
- `tests/Vista.Tests`: the tests, which reference Core only. Folders mirror Core's.

## Packages

### Core

- `Camera`: the camera's pose (`CameraState`), angle and rotation maths, free-cam motion and fly speed, screen projection, and the rules every frame written must keep.
- `Display`: what the editor draws, as numbers: the camera glyph, track paths and turn heat, marker hit tests, the timing graph and its view, tick spacing, row text fitting, edge scrolling and panel widths.
- `Editing`: turning input into edits: pose field limits, gizmo matrices and drags, clicks on markers and list rows, block moves of dragged rows, and wheel notches.
- `Guide`: reading the User Guide's Markdown pages and index into blocks and topics.
- `Scenes`: scenes, playlists and presets as data, their edits and names, their place in the world, and their files.
- `SelfTest`: the rules that decide whether each `/vista selftest` check passed, and its report lines.
- `Session`: the mode, selection, undo history, Edit preview and scrub head, and the scene's tracks in the world.
- `Tracks`: the track and its control points and anchor, editing its points and timing, and evaluating it at a moment.
- `Tracks/Aiming`: where the camera looks and which way is up: recorded aim, direction of travel, Look At, watched and followed characters, and smoothing.
- `Tracks/Playback`: the Director, and playing a track or a playlist frame by frame.
- `Tracks/Spline`: the Catmull-Rom path and its arc-length table.
- `Tracks/Timing`: timing keys, compiling them from speeds and holds, the timing curve, easing, and the timed aim channels.

### Plugin

- `Editor`: what's drawn over the game in Edit and View: the overlay, the point and anchor gizmos, editor keys and colours.
- `Game`: reading and writing the game: the camera and its hook, the free-cam, input blocking, movement lock, the UI toggle, ground and nearby characters, and faults.
- `SelfTest`: running `/vista selftest` in the game.
- `Session`: carrying the session's mode changes out in the game, and the save folder and scene files.
- `Ui/Main`: the main Vista window, with its Hierarchy and Playlist panels.
- `Ui/Widgets`: the ImGui pieces the windows share.
- `Ui/Windows`: every other window: Camera, Point, Timing, Target, Watch and Follow Target, User Guide, Setup and Welcome.
- `Guide` and `Demo` hold the User Guide's Markdown pages and the demo scene, embedded in the plugin.

## Helper homes

Put general logic in the home for its kind; add a home here when a new kind needs one.

### Angles, rotations and vectors

- `Camera/Angles`: degrees and radians, wrapping to half a turn, and the shortest signed difference between two angles.
- `Camera/CameraRotation`: rotations from yaw, pitch and roll and back, from a forward and up, the forward and up of a rotation, squaring an up to a forward, the upright up, and the shortest rotation between two directions.
- `Camera/CameraState`: a camera frame from a rotation or from angles, and its roll.
- `Camera/FreeCamMotion`: a free-cam step, turn and roll, the look-at distance, and a look-at point from angles or a rotation.
- `Editing/PoseMatrix`: a pose to and from the gizmo's matrix.

### Scalars and curves

- `Tracks/Timing/Hermite`: the cubic Hermite basis.
- `Tracks/Search`: the binary search over an ascending array that curves and tables use to find their interval.
- `Tracks/Spline/CatmullRom` and `ArcLengthTable`: the path through points, its derivative, and walking it by distance.

### Limits

- `Editing/EditLimits`: the range of each pose field (pitch, angle, FoV, coordinates): a value that isn't finite changes nothing, a finite one is clamped or wrapped.
- `Tracks/TrackEditing`: each track setting's default and range (speed, seconds, aim height, look ahead, smoothing), applied by its setter.

### Names

- `Scenes/SceneNames`: checking scene, preset and track names, and suggesting the next free name or a copy's name.

### List edits

- `Editing/RowPicking`: a click on a list row, with Ctrl and Shift, and what a drag carries.
- `Editing/BlockMove`: the new order when rows are dragged as a block, and applying an order to a list.
- `Scenes/SceneEditing` and `PlaylistEditing`: finding a track or playlist entry by id, refusing an unknown one.

### Formatting

- `Display/Ticks`: tick spacing and the label format for it.
- `Display/RowFit`: cutting a row's name to fit with an ellipsis, and scrolling it while hovered.

### Hit tests

- `Display/MarkerHitTest` and `TrackMarkerHitTest`: the marker nearest a click within a radius.

### Timing

- `Tracks/TrackEditing`: which timing key is a point or a hold end, and the keys a leg runs between.
- `Tracks/TrackEvaluator`: leg lengths and times, point times, and distance and speed along the path at a time.

### Playback clock

- `Tracks/Playback/PlaybackClock`: a cycle's length, and mapping a playback clock to a shot time and back.

### UI widgets

- `Ui/Widgets/IconButton`: frameless icon buttons with tooltips, toggles, row actions, row hover, the not-found warning, and icon widths.
- `Ui/Widgets/PoseGrid`: the Point and Camera windows' shared layout and fields.
- `Ui/Widgets/BorderedField`, `PendingField`, `LiveDrag` and `TextEdit`: a bordered number field, a field applied on release, a field previewed live as one undo step, and text edited in place.
- `Ui/Widgets/DragRows` and `RowText`: dragging list rows and reading their click, and a row's fitted name.
- `Ui/Widgets/PopupStyle`, `CharacterPicker` and `Refusal`: the popup style, the character drop-down, and logging a refusal.

### Colours

- `Ui/Widgets/UiColours`: the Vista window's colours.
- `Editor/EditorColours`: the overlay's colours.

### Gizmos

- `Editor/Gizmo`: the ImGuizmo calls the point and anchor gizmos share.

### Game access

- `Game/CameraAccess`: reading and writing the world camera.
- `Game/CharacterTable` and `Ground`: the characters loaded nearby, and the ground under a point.
- `Game/PhysicalKeys`: keys read from their physical state.

### Test fixtures

- `Fixtures`: what's used across test areas: control points and tracks through them, Guard and the tracks watching them, going live, generators (points, path tracks, target settings, frame steps), finite-difference slopes, the counterexample printer, step and twist measures, and vector assertions.
- `Session/SessionFixtures`: the sessions the session tests start from, and track and entry ids by index.
- `Tracks/Timing/TimingFixtures`: key times and key drags.
- `Tracks/Playback/PlaybackFixtures`: the straight track the playback tests play.
- `Scenes/SceneFixtures` and `TempFolder`: scenes told apart by name, and a scene folder in a temporary directory.
