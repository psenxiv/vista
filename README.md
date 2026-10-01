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
- **Playlists and the switchboard.** Line tracks up in playlists with repeat counts, then cut between tracks and playlists live from a switchboard of ten slots, with the game UI hidden if you want.
- **Modes.** Off leaves the game alone, View shows your scene over the normal camera, Edit is where you build, and Live plays. Your scene stays put when you change zone.

## Building

Needs the .NET 10 SDK and Dalamud's development assemblies.

```sh
make build     # Debug build, to load as a dev plugin
make test      # Core tests
make package   # Release build and latest.zip
```

## Feedback

Tell us what's working and what isn't in the [feedback form](https://forms.gle/p9hAJvQZT7qZLN5T7). It's anonymous and takes a couple of minutes.

## Licence

MIT. See [LICENSE](LICENSE).
