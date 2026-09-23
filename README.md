<p align="center"><img src="images/icon.png" width="96" alt="Vista"></p>

# Vista

Create smooth, cinematic camera paths in FFXIV, organise them into scenes, and play them back live.

Vista is a [Dalamud](https://github.com/goatcorp/Dalamud) plugin. Open it with `/vista`.

## What it does

- **Tracks.** Fly a free camera and drop points to lay out a path. Set each leg's timing, holds and
  easing, and play the path forwards, in reverse or ping-pong, once or on a loop.
- **Aim.** A track looks along the aim you recorded, along its path, at a fixed point (Look At), or
  keeps a character in frame (Watch Target). A one-point track can ride along with a character
  (Follow Target).
- **Scenes.** Tracks sit on anchors you can move and turn, so a whole setup can be picked up and
  placed somewhere else.
- **Playlist and Live.** Line tracks up in a playlist with repeat counts, then play it live, with the
  game UI hidden if you want.
- **Modes.** Off leaves the game alone, View shows your scene over the normal game camera, Edit is
  where you build it, and Live plays it. Your scene stays put when you change zone.

## Keys

These work in Edit mode, and Space and Ctrl + Space work in Live too.

| Key | Does |
|---|---|
| W A S D | Fly forward, left, back, right |
| E / Q | Fly up / down |
| Ctrl + Q / Ctrl + E | Roll left / right |
| Shift | Fly faster while held |
| Space | Play / pause |
| Ctrl + Space | Restart from the beginning |
| Mouse wheel | Change fly speed |
| Mouse | Look around, as with the game camera |
| Backtick | Add a point at the camera, at the end of the track |
| Alt + Backtick | Add a point after the selected one |
| Ctrl + Backtick | Replace the selected point with the camera |
| R | Switch the gizmo between move and rotate |
| G | Colour the path by how fast the camera turns, in Edit and View |
| Alt while dragging an anchor | Move the anchor alone, leaving its points in place |
| Ctrl while dragging a timing key | Move every later key with it, lengthening or shortening the track |
| Ctrl + click | Add to the selection, or remove from it |
| Shift + click | Add everything from the last click to this one |
| Delete / Backspace | Delete the selected points |
| Ctrl + Z / Ctrl + Y | Undo / redo |

## Building

Needs the .NET 10 SDK and Dalamud's development assemblies.

```sh
make build     # Debug build, to load as a dev plugin
make test      # Core tests
make package   # Release build and latest.zip
```

## Licence

MIT. See [LICENSE](LICENSE).
