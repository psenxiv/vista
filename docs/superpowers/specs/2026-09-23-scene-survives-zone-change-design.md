# Vista — the scene survives a zone change (Phase 4.1)

Date: 2026-09-23
Status: Approved

A zone change no longer clears the scene. Supersedes the **Zone change** section of
`2026-09-23-off-mode-and-zone-reset-design.md`; everything else in that document stands.

## Why

3.e.6 was specced before scenes could hold meaningful work. Clearing on a territory change now
destroys the scene, every track, the playlist, the anchors and the whole undo history, with no
confirmation and no way back. Once saving and loading land, swapping scenes is something the
user does deliberately; a teleport is not that.

## Behaviour

- A territory change does nothing to the scene. Tracks, points, playlist, anchors, selection,
  scrub head and undo history all survive.
- The camera is still handed back during the loading screen. That already happens by a separate
  path: the `BetweenAreas` / `BetweenAreas51` condition flags release to Off, which also stops
  Live playback. A teleport sets those flags, so this needs no change.
- A scene built in another zone is drawn as normal, at its recorded world coordinates. It may
  therefore appear underground, in the sky, or far from the player. That is accepted: the scene
  is moved by dragging its anchor, as it always has been.
- A Follow Target track whose character is not in the new zone behaves as it already does when a
  target is lost — the warning triangle shows and the shot keeps its recorded aim.

## Not doing

- **No territory recorded on `Scene`.** Hiding a scene outside its own zone, or warning about
  one, would need that field, and the field would then land in the save format. That is a
  decision for saving and loading (Phase 3.f), not one to make early here.
- **No confirmation prompt**, and no "clear scene" affordance. Clearing stays reachable exactly
  where it already is.

## Architecture

- `Plugin.OnTerritoryChanged` and its `ClientState.TerritoryChanged` subscription are removed,
  along with the matching unsubscribe in `Dispose`.
- `CameraSession.ClearScene(string)` and `SessionState.ClearScene()` are **kept**. They are still
  correct, still tested, and are what an explicit scene swap will call in 3.f. This spec removes
  the caller, not the capability.

## Testing

No Core change, so no new Core tests; `SessionState.ClearScene` keeps the cover it has. The
behaviour is plugin-side and goes on the Phase 4 in-game checklist: teleport with a built scene
and confirm the tracks, playlist and undo history are all still there, and that the camera was
handed back on the way.
