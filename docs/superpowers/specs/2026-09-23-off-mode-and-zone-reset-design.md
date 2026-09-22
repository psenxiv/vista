# Vista — Off mode and zone reset (Phase 3.e.6)

Date: 2026-09-23
Status: Approved

Off comes back beside View, Vista starts in it, and a zone change clears the scene. Builds on
`2026-09-22-view-mode-design.md`. This document wins where they differ.

## Modes

- The mode drop-down reads **Off · View · Edit · Live**.
- **Off:** the game has its camera and character, nothing is locked, and Vista draws nothing in the
  world. Vista starts in Off.
- **View** is unchanged: the game has its camera and the scene is drawn, view-only.
- Off and View are the two "released" modes. Play or Restart from either goes Live; Edit from
  either enters Edit.
- Releasing the camera (`/vista release`, a zone change, logout, an area transition, choosing Off)
  goes to Off. Choosing View in the drop-down goes to View.

## Zone change

- When the territory changes, Vista releases the camera to Off and replaces the scene with a new,
  empty one: one empty track, no playlist, no anchors. Undo history is cleared, and so are the
  selection and the scrub head.
- Area transitions within the same territory (an aethernet hop, a cutscene) only release the
  camera, as today.

## Architecture

- `CameraMode` gains `Off` as its first value (the default), before `View`.
- `SessionState.Release(CameraMode to = Off)` goes to Off or View; `Released` is true in either.
  `EditOutcome.FromView`, `PlayOutcome.StartedFromView` and `CuedFromView` become `FromGame`,
  `StartedFromGame` and `CuedFromGame`.
- `SessionState.ClearScene()` releases to Off and starts a new scene; `EditHistory.Clear()`.
- The plugin's territory handler calls it; the drop-down's View calls `Release(View)`.

## Testing

Core: starting in Off; releasing to Off and to View, from each owned mode and between the two;
input unlocked in both; Play and Edit from Off; `ClearScene` resetting the scene, history,
selection and scrub head. The overlay and the zone reset are checked in game.
