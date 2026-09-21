# Cinematic Cam — Curve Editor (Phase 2c-2) Design

Date: 2026-09-22
Status: Approved in brainstorming, awaiting written-spec review

2c-2 adds control over a shot's pacing: easing presets per leg, and a graph for
shaping the timing curve by hand. It builds on the timing model in
`2026-09-20-cinematic-cam-design.md` (Timing) and the editor in
`2026-09-21-editor-design.md`. Where they differ, this document wins.

The plugin is unreleased, so formats and defaults change freely; nothing here is
kept for compatibility.

## Terms

- **Key** — a timing key: a time and a place on the path.
- **Point key** — a point's first key, reached when the camera arrives there.
- **Hold end** — a point's second key, where a hold ends. Present only with a hold.
- **Inner key** — a key between two points.
- **Leg** — the stretch of timing from one point's last key to the next point's
  first key. Row *i*'s leg arrives at point *i*.
- **Side** — the half of a key facing one leg: a key's in side faces the leg before
  it, its out side the leg after.

## Scope

Decided 2026-09-22:

- **Easing presets per leg**, set in the Timing window.
- **A timing graph**, in its own window, for dragging keys and handles and adding
  keys between points.
- Built in two parts. Part 1 is the Core model, the edits and the presets, with no
  UI and no in-game check. Part 2 is the Timing window. One in-game checklist
  follows Part 2.

Out of scope: weighted tangents, zooming and panning the graph, a speed graph, and
a Delete key.

## Timing model

`TimingKey` replaces its single mode with one per side:

```csharp
record TimingKey(float Time, float Position, TangentMode InMode, TangentMode OutMode,
                 float InTangent, float OutTangent);
```

`Position` is unchanged: control-point units, where the whole part is the segment
and the fraction is the share of its arc length. An inner key has a fractional
position. `InTangent` and `OutTangent` matter only on a `Manual` side.

`TimingCurve` works out each side from its own mode:

| Mode | Shown as | Slope on that side |
|---|---|---|
| `Auto` | Smooth | blended from the neighbouring legs, as today; one-sided at the shot's ends |
| `Linear` | Linear | its own leg's average speed |
| `Flat` | Flat | 0 |
| `Manual` | (a dragged handle) | the stored value |

The monotone clamp still applies per leg, so the curve never runs backwards, and a
side facing a hold is always flat. New keys are `Auto` on both sides.

Keys between points are allowed. The 2c-1 refusal of such tracks is removed.

## Easing presets

A preset sets the two sides that bound its leg: the out side of the leg's first
key and the in side of its last key.

| Preset | Out side of the first key | In side of the last key |
|---|---|---|
| Smooth (default) | Auto | Auto |
| Linear | Linear | Linear |
| Ease in (starts slow) | Flat | Auto |
| Ease out (ends slow) | Auto | Flat |
| Ease in-out | Flat | Flat |

A leg whose two sides match no row, such as one with a dragged handle, reads
**Custom**. A preset leaves a leg's inner keys in place. Setting easing never
changes a leg's time.

Modes, not slopes, are stored, so a preset stays correct when a leg's time changes
or a point moves.

## Timing window

```
┌ Timing ─────────────────────────────────────────────────────────┐
│ Leg 2 → 3  [Ease out ▾]    Key: 7.5 s  [Smooth][Linear][Flat][🗑] │
├─────────────────────────────────────────────────────────────────┤
│ 3 ┤·························●━━━━━●                              │
│   │                    ╭───╯                                    │
│ 2 ┤·············◆─────╯        (flat run = hold at 3)           │
│   │         ╭──╯                                                │
│ 1 ●━━━━━━━━╯                   │ playhead                       │
│   └──────┬──────────┬──────────┼───────────┬──── 0 … 15.0 s     │
└─────────────────────────────────────────────────────────────────┘
```

- **Opening.** A graph icon on the track editor's top row opens it. It is resizable,
  remembers where it was placed, has a close button, and Escape does not close it.
- **Axes.** Time across, distance along the path up, always fitted to the whole
  shot. Each point has a labelled horizontal line at its distance. A straight run
  is constant speed; a flat run is a hold.
- **Playhead.** A vertical line at the scrub head. Clicking or dragging along the
  time axis scrubs, exactly as the scrub bar does.
- **Keys.** A point key is a dot with its number; clicking it selects the point, and
  the overlay and the Point window follow. A hold end is a plain dot. An inner key
  is a diamond.
- **Legs.** Clicking a stretch of curve away from any key selects that leg. The top
  row's easing drop-down applies to the selected leg.
- **Dragging keys.** A point key or hold end moves in time only, squeezing its
  neighbours: the legs either side change and the shot's length does not. An inner
  key moves in time and distance, kept between its neighbours so the curve never
  runs backwards. Each drag previews live and is one undo step.
- **Adding and removing keys.** Double-click the curve to add an inner key there; the
  curve's shape stays as it was. The top row's trash icon, or right-click, deletes
  an inner key; on a hold end it removes the hold. A point key cannot be deleted
  here; deleting the point does that.
- **Handles.** The selected key shows a handle on each side that has a leg. Dragging
  one makes that side `Manual`, and its leg reads Custom. The two handles move
  together unless broken; right-click offers Break and Unify. A handle sets slope
  only. A side facing a hold is always flat, and its handle is hidden.
- **Key buttons.** Smooth, Linear and Flat set both sides of the selected key.
- **Live.** The graph is read-only and the playhead follows playback. Scrubbing works
  as on the scrub bar.
- **Keyboard.** Ctrl + Z and Ctrl + Y work as everywhere in the editor. There is no
  Delete key: Dalamud's key handling makes it unreliable.

The track editor's Leg and Hold fields stay, as another view of the same keys.

## Point edits and inner keys

Decided 2026-09-22: inner keys are kept and remapped, not cleared. An inner key
holds its time and its share of its leg's distance.

| Edit | Inner keys |
|---|---|
| Leg field | spread in proportion over the leg's new time; later keys shift, as today |
| Dragging a point key or hold end | both neighbouring legs change, and their inner keys keep their share of each leg's time |
| Add to end | the new leg has none |
| Add after selected | the new point splits the leg's time as today; each inner key goes to the half its time falls in, keeping its share of distance rescaled to that half, clamped so the curve never runs backwards |
| Delete a middle point | both legs' inner keys join the merged leg, each keeping its time and its share of the combined distance |
| Delete the first or last point | that leg's inner keys go with it |
| Reorder | leg times, easing and inner keys stay in their slots; holds travel with their point, as today |
| Overwrite, gizmo, Point window fields | timing unchanged; inner keys keep their fractions and follow the new path |

Keys stay at least 0.05 s apart, so times always increase. An edit that would
break that clamps the dragged key rather than refusing.

## Undo

Every timing change goes through the session's undo history. Graph drags use the
live-edit path the Point window's fields use, widened from a point to the whole
track: previews while dragging, one undo step on release, and undo mid-drag
reverts it.

## Architecture

**Core**

- `TimingKey`, `TimingCurve` — a mode per side.
- `LegEasing` — the presets, mapping to side modes and reading Custom back.
- `TimingEditing` — pure operations: set a leg's easing, move a key, add or delete
  an inner key, set a key's modes, drag a handle, break and unify handles.
- `TrackEditing` — the inner-key rules above.
- `TimingGraph` — time and distance to pixels and back, and which key, handle or
  leg is under the cursor.
- `TrackEvaluator` — exposes each point's distance and the distance at a time.
- `SessionState` — the live edit widened to the track; timing selection (a key or a
  leg) alongside the point selection.

**Plugin**

- `Ui/TimingWindow.cs` — the window, drawn with ImGui's draw list.
- `Ui/TrackEditorWindow.cs` — the icon that opens it.

## Testing

Core under TDD: the curve with each side mode and its monotone clamp; presets both
ways, including Custom; every edit in the inner-key table; squeeze drags and the
0.05 s clamp; the graph's mapping and hit-testing; the widened live edit and
undo. In game, one `CHECKLIST.md` after Part 2, for the user to work through.
