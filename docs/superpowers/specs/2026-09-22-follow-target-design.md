# Vista — Follow Target (Phase 3.e.5)

Date: 2026-09-22
Status: Approved

A camera that rides along with a character at a fixed offset. The aim mode called Follow Target in
3.e.3 is renamed **Watch Target**, and **Follow Target** becomes this new mode. Builds on
`2026-09-22-look-at-and-follow-target-design.md` (3.e.3) and `2026-09-22-view-mode-design.md`.
This document wins where they differ.

Out of scope: following along a path (a Follow track has one point), following vehicles or
objects that aren't characters, and blending into or out of a follow.

## Rename

- The aim mode that keeps the camera pointed at a character is **Watch Target** everywhere: the
  aim menu, its dialog's title ("Watch Target"), the aim icon's tooltips ("Watch Target: Name",
  "Watch Target: choose a character"), the pencil's tooltip ("Edit Watch Target") and the code
  (`AimMode.WatchTarget`, `WatchTargetWindow`).
- Nothing else about it changes.

## Follow Target

**The aim menu** reads: Recorded aim · Direction of travel · Look At · Watch Target ✎ ·
Follow Target ✎.

**One point**

- A Follow track has exactly one point. Follow Target is disabled in the menu for a track with
  more than one point, with the tooltip "Follow Target needs a track with one point".
- While a track is under Follow Target, adding a second point is refused, with the reason
  "A Follow Target track has one point".
- Its timing is a snap point's: the point's hold, the entry's Repeats and the track's Loop decide
  how long it stays on.

**The offset**

- The point is stored as an offset from the character: its position and its recorded yaw relative
  to the character's position and facing; pitch, roll and FoV as they are.
- Capturing the point (Backtick or + Add) while under Follow Target records the camera relative to
  the character where they stand now. It is refused with "Choose a character to follow" when no
  character is chosen, and with "Character not found" when they can't be found.
- **Switching to Follow Target, or choosing a new character, keeps the camera where it is:** when
  the track has its point and the character is found, the point is re-expressed relative to them
  as they stand now. When they can't be found, the point's numbers are kept and read as an offset.
- Editing the point (the gizmo, the Point window, overwrite) edits it where it is drawn, relative
  to the character where they stand now.

**The dialog** (opened with its pencil, and on choosing Follow Target when no character is chosen)

- Titled "Follow Target", floating, the same layout as Watch Target's: search, the character list
  ("Name · World" or "Name · NPC"), then:
  - **Turn with character** (on by default): on, the offset turns as they turn (a chase camera);
    off, the offset keeps the facing the character had when playback, a preview or a scrub
    started, so the camera only moves with them.
  - **Look at character** (off by default): on, the camera always points at the character at the
    aim height, as Watch Target does; off, it keeps the point's recorded aim, turned with the
    character when Turn with character is on.
  - **Aim height**, used when Look at character is on.
  - **Smoothing**, easing both the camera's position and its aim as Watch Target eases its aim.
  - Done.
- Every change in it is one undo step, and it is disabled unless editing.
- The character, aim height and smoothing are the track's one shared set, as for Watch Target.

**When the character can't be found**

- The camera stays where it last was, looking as it last did. If it never found them in this
  playback, it sits at the point read as an offset from the track's anchor.
- The aim icon turns red with "Name (Not found): using the last position", and the playlist warns as
  for Watch Target.

**Editing**

- While edited, the point, its camera glyph and its aim line are drawn at the character plus the
  offset, moving as they move. The track's anchor isn't drawn and takes no clicks for a Follow
  track, and anchor moves don't affect it.
- In View the same drawing applies, view-only.
- Scrubbing, previews and Live all place the camera from the character where they are now,
  through the smoothing, which starts afresh on start, seek, scrub, cut and preview as for Watch
  Target.

**The aim icon** is drawn blue under Follow Target when the character is found, red otherwise, as
under Watch Target, with tooltips "Follow Target: Name", "Name (Not found): using the last
position" and "Follow Target: choose a character".

## Architecture

**Core**

- `AimMode`: `FollowTarget` becomes `WatchTarget`, and a new `FollowTarget` is added.
- `Track` gains `FollowTurns` (default true) and `FollowLooks` (default false), with setters.
- `LoadedCharacter` gains `Facing` (yaw, radians); `IAimTargets` returns position and facing.
- A pure `FollowOffset` converts between a world camera point and an offset given the character's
  position and facing.
- `AimTracker` (or a sibling) resolves a Follow track's camera per frame: position and aim from the
  character, the offset and the switches, through the smoother, holding the last frame when lost;
  the playbacks and `FrameAt` use it as they use the Watch Target aim.
- `SessionState`: the one-point refusals, capturing and editing the offset, and re-expressing the
  point on switching mode or character.

**Plugin**

- `CharacterTable` reads each character's facing.
- `WatchTargetWindow` (renamed) and a `FollowTargetWindow` sharing the picker.
- The aim menu's new entry and pencil, the one-point greying, and the icon states.
- The overlay draws a Follow track's point at the character and skips its anchor.

## Testing

Core under TDD:

- the rename carried by the existing tests;
- the offset round trip, with and without turning;
- a Follow track's camera: position and recorded aim turning with the character, fixed facing when
  Turn with character is off, Look at character, smoothing and its resets;
- lost characters: holding the last frame, and the anchor fallback;
- the one-point refusals, capture refusals, re-expressing on switch and on a new character, and
  undo for every setting.

Then the in-game checks join `CHECKLIST-3e3.md`.
