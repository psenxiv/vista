# Dev setup (XIV on Mac)

## One-time

1. Launch the game through XIV on Mac.
2. Open Dalamud settings in game: `/xlsettings`.
3. Experimental tab: enable **Plugin dev mode**.
4. Under **Dev Plugin Locations**, add the path to the **DLL itself**, not the
   folder containing it. A directory path is rejected with "not a valid path to
   a potential Dev Plugin". The Wine prefix maps `Z:` to the macOS root:

       Z:\Users\<your-macos-username>\code\ffxiv-projects\cinematic-camera\src\Vista.Plugin\bin\Debug\Vista.dll

   Substitute your own username. `Z:` maps to the macOS root, so this is just
   the absolute path to the repo with backslashes.

   Note there is no framework-named subdirectory — DalamudPackager flattens the
   output. The filename does not change between builds, so this path is set
   once and stays valid.
5. Save and close.

## Each iteration

1. On macOS: `./build.sh`
2. In game: `/xlplugins` -> **Dev Tools** tab -> find **Vista** ->
   click the reload icon.
3. No game restart required. If reload does not pick up changes, record that
   here and restart the game instead.

**Reload status, confirmed 2026-09-20:**

- **Manual reload works.** Clicking the reload icon unloads and reloads in about
  two seconds, no game restart. This is the normal loop.
- **Automatic reloading does not work**, even when Dalamud reports it as
  enabled. Dalamud watches the DLL with a `FileSystemWatcher` on
  `NotifyFilters.LastWrite`, which needs `ReadDirectoryChangesW`. Wine does not
  deliver those events for the `Z:` drive when a native macOS process writes the
  file, so the watcher never fires. Verified by `touch`ing the DLL directly: no
  reload after two minutes.

So every iteration costs one click. Build, then click reload.

## Reading the log

Readable from macOS while the game runs, so build-side verification needs no
game restart:

    ~/Library/Application Support/XIV on Mac/logs/dalamud.log

Filter to this plugin:

    grep Vista ~/Library/Application\ Support/XIV\ on\ Mac/logs/dalamud.log | tail -40

## Build environment reference

- Dalamud 15.0.3.5, targeting **net10.0**.
- Dev reference assemblies: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev`
- `build.sh` exports `DALAMUD_HOME` to that path. Always build through it.
- All three projects target .NET 10, matching SDK 10.0.301 and runtime 10.0.9.
