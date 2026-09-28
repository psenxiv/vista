<p align="center"><img src="images/icon.png" width="96" alt="Vista"></p>

# Vista

Create smooth, cinematic camera tracks in GPose or out in the world, organise them into scenes, and play them back live.

Install it with the steps below, then type `/vista` to open it. The User Guide is under the **?** at the top right of the Vista window, or **Help** in its menu bar.

## Demo

https://github.com/user-attachments/assets/a39c8d1c-5b92-4186-be06-30fb5b590567

## Installing

1. Type `/xlsettings` in chat and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste the following into the empty field, click **+**, and save.

```
https://raw.githubusercontent.com/psenxiv/vista/main/repo.json
```

3. Type `/xlplugins`, search for Vista and install it.

## What it does

- **Tracks.** Fly a free camera and drop points to lay out a path, then set each leg's timing, holds and easing. Play it forwards, in reverse or ping-pong, once or on a loop.
- **Aim.** Look along your recorded aim, along the path, at a fixed point, or keep a character in frame. A one-point track can ride along with a character.
- **Scenes and presets.** Tracks live in scenes that save as you work and open from a searchable list. Move a whole setup by its anchor, and save any track as a preset to reuse in other scenes.
- **Playlist and Live.** Line tracks up with repeat counts and play them live, with the game UI hidden if you want.
- **Modes.** Off leaves the game alone, View shows your scene over the normal camera, Edit is where you build, and Live plays. Your scene stays put when you change zone.

## Keys

These work in Edit mode. Space and Ctrl + Space also work in Live, and G in View. Escape only works in Live.

| Key                              | Does                                                              |
| -------------------------------- | ----------------------------------------------------------------- |
| W A S D                          | Fly forward, left, back, right                                    |
| E / Q                            | Fly up / down                                                     |
| Ctrl + Q / Ctrl + E              | Roll left / right                                                 |
| Alt + R                          | Level the camera's roll                                           |
| Shift                            | Fly faster while held                                             |
| Space                            | Play / pause                                                      |
| Ctrl + Space                     | Restart from the beginning                                        |
| Escape                           | Bring back the game UI Vista hid                                  |
| Mouse wheel                      | Change fly speed                                                  |
| Mouse                            | Look in any direction                                             |
| Backtick                         | Add a point at the camera, at the end of the track                |
| Alt + Backtick                   | Add a point after the selected one                                |
| Ctrl + Backtick                  | Overwrite the selected point with the camera                      |
| R                                | Switch the gizmo between move and rotate                          |
| G                                | Colour the path by how fast the camera turns                      |
| Alt while dragging an anchor     | Move the anchor alone, leaving its points in place                |
| Ctrl while dragging a timing key | Move every later key with it, lengthening or shortening the track |
| Ctrl + click                     | Add to the selection, or remove from it                           |
| Shift + click                    | Add everything from the last click to this one                    |
| Delete / Backspace               | Delete the selected points                                        |
| Ctrl + Z / Ctrl + Y              | Undo / redo                                                       |

## Building

Needs the .NET 10 SDK and Dalamud's development assemblies.

```sh
make build     # Debug build, to load as a dev plugin
make test      # Core tests
make package   # Release build and latest.zip
```

## Licence

MIT. See [LICENSE](LICENSE).
