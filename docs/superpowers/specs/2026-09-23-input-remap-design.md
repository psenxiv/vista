# Vista — free-cam input remap (Phase 4.3)

Date: 2026-09-23
Status: Approved

One rotation of the free-cam key scheme. The three changes depend on each other and cannot land
separately: roll has to vacate Q/E before up/down can move onto them, and up/down has to leave
Space before Space can drive the transport.

## The scheme

| | before | after |
|---|---|---|
| Q / E | roll left / right | **down / up** |
| Ctrl + Q / E | — | **roll left / right** |
| Space | fly up | **play / pause** |
| Ctrl + Space | — | **restart** |
| C | fly down | **unbound** |

W/A/S/D, Shift to sprint and mouse-look are unchanged. E is up and Q is down, keeping the
polarity roll already had, where E was the positive direction.

## Where each one lives

**Q/E and Ctrl+Q/E** are continuous, read every frame in `FreeCam.ReadInput` and `ReadRoll`.
`ReadInput` gains the Q/E pair on the up axis when Ctrl is *not* held and loses Space and C;
`ReadRoll` returns zero unless Ctrl *is* held. Both keep reading through `Plugin.KeyState`, as
they do now — Vista does not hide Q or E, and does not need to, because the character's movement
is already locked while it owns the camera.

**Space and Ctrl+Space** are discrete presses and belong in `EditorKeys`, which already
edge-detects, hides keys from the game and ignores presses while text input is active.

## Widening EditorKeys past Edit mode

`EditorKeys.Update` currently returns immediately unless the mode is Editing, so every binding it
owns is Edit-only. Space has to work in Edit **and** Live.

- The early return widens to "released" — it returns when the mode is Off or View, so the body
  runs in Edit and Live.
- Ownership becomes per-key rather than per-mode. Space is claimed in Edit and Live; every
  existing binding (Backtick, Ctrl+Backtick, Alt+Backtick, Ctrl+Z, Ctrl+Y, R, Delete, Backspace)
  is claimed only in Edit, exactly as today.
- C leaves the watched list entirely, so Vista stops hiding it and the game gets it back.
- Space joins the watched list and is hidden whenever Vista claims it, so the character cannot
  jump when a take starts. A key Vista hides reads as false through `Plugin.KeyState`, which is
  why `FreeCam` must not read Space any more — it no longer does.

## `IsPlaying` moves to Core

Space toggles whatever the Play button would do, so it needs the same predicate the button uses.
That predicate is currently written inline in `TrackEditorWindow.DrawTransport`:

```csharp
session.Previewing || (session.Mode == CameraMode.Live && !session.Director.IsPaused && !session.Director.IsFinished)
```

Rather than copy it into `EditorKeys`, it becomes `SessionState.IsPlaying`, with a
`CameraSession` forwarder. Every part of it is already in Core. This is the "keep `Vista.Plugin`
thin" rule applied: it is a decision assertable without ImGui or the game, so it moves and gets a
test, and the two call sites read the one owner.

Space calls `StopPlay()` when `IsPlaying`, `StartPlay()` otherwise. Ctrl+Space calls
`RestartPlay()`. Neither does its own gating: the session refuses and logs, as the buttons
already rely on.

## Not doing

- No rebinding UI. The scheme stays fixed.
- Space does not go Live from Off or View. It acts only where Vista already owns the camera.
- No change to Escape, Shift, or the mouse.

## Testing

Core gets `IsPlaying` cases: false when released, false while previewing is off and the mode is
Edit, true while previewing, true when Live and running, false when Live and paused, false when
Live and finished. Expected values come from the mode and director state each case sets up, not
from calling the predicate.

The bindings themselves are plugin-side and go on the Phase 4 in-game checklist: each of Q, E,
Ctrl+Q, Ctrl+E, Space and Ctrl+Space in Edit; Space and Ctrl+Space in Live; that C no longer
flies the camera down; and that pressing Space does not make the character jump.
