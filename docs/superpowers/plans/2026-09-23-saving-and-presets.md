# Saving, loading and presets — plan

Spec: `docs/superpowers/specs/2026-09-23-saving-and-presets-design.md` (cited as §n).

Rules for every task: read `CLAUDE.md` first. Derive expected values by hand with the derivation in
a comment; tolerances at the assertion; mutation-check new tests. Source and tests in separate
commits; one-line conventional messages, no body, no trailers. Build with `make build`, test with
`make test`, never bare `dotnet build`. Doc comments one line.

## Wave 1 (parallel, worktrees)

### A. Names, JSON and the debounce — new files only

`src/Vista.Core/Scenes/SceneNames.cs` (§2.1):

```csharp
public static class SceneNames
{
    public const int MaxLength = 64;
    /// Why the trimmed name can't be a file name, or null: "Enter a name", "That name is too long",
    /// "That name can't be used as a file name".
    public static string? Refusal(string name);
    /// True when another name in existing matches, ignoring case.
    public static bool Taken(string name, IEnumerable<string> existing);
    /// "Scene 1", "Scene 2", … the first stem + " N" (N from 1) not taken.
    public static string NextFree(string stem, IEnumerable<string> existing);
    /// "X copy", then "X copy 2", "X copy 3", … the first not taken.
    public static string CopyOf(string name, IEnumerable<string> existing);
}
```

`src/Vista.Core/Scenes/Preset.cs`: `public sealed record Preset(Track Track, float Yaw);` — the track
local to its anchor (its own `Anchor` ignored), and the anchor's world yaw (§3).

`src/Vista.Core/Scenes/SceneJson.cs` (§2.2): `public const int Format = 1;`
`string Write(Scene)`, `Scene Read(string)`, `string WritePreset(Preset)`, `Preset ReadPreset(string)`.
Use private DTO records with `System.Text.Json` (Vector3 has fields, so map it explicitly); enums as
strings; `Hidden` as a list of ids; a preset's track name is not stored (the file name is). `Read`
throws `InvalidDataException` for malformed JSON or `format` ≠ 1. Tests: round trips of a scene
exercising every Track, PointTiming, PlaylistEntry and Scene field with non-default values; a
preset round trip; wrong format and garbage both throw.

`src/Vista.Core/Session/SaveDebounce.cs` (§2.4):

```csharp
public sealed class SaveDebounce
{
    public const double DelaySeconds = 1.0;
    public SaveDebounce(Scene saved);
    /// Feeds the scene at time now, in seconds; true once it has differed from the saved scene,
    /// unchanged by reference, for DelaySeconds.
    public bool Due(Scene current, double now);
    /// Records scene as saved.
    public void Saved(Scene scene);
}
```

### B. Loading and presets in the session

In `SessionState` (§2.3, §3.2):

- `public string? LoadScene(Scene scene)`: refused in Live ("A scene can't be loaded while Live.");
  otherwise stops any preview, drops any live edit without recording it, sets the scene, edits its
  first track, clears the point, key, leg and anchor selection, puts the scrub head at 0, clears the
  world caches and the undo history. Mode untouched.
- Delete `ClearScene` here and in `CameraSession`; replace its test with LoadScene tests.
- `src/Vista.Core/Scenes/Presets.cs`:
  - `Preset From(Scene scene, Guid trackId)`: the track with `Anchor = Anchor.Origin` and its
    anchor's world yaw.
  - `(Scene Scene, Guid Added) Place(Scene scene, Preset preset, Vector3 ground)`: a new track with a
    new id and `AnchorPlaced` true, its anchor at `ground` with the preset's yaw, expressed relative to
    the scene anchor; if the scene anchor isn't placed, it is placed at `ground` with yaw 0 first.
    Appended to the Hierarchy.
- `SessionState.AddPreset(Preset preset, Vector3 camera)`: ground is `groundBelow(camera) ??
  camera.Y`; one undo step via `CommitScene`; edits the added track.
- `CameraSession`: `LoadScene(Scene)` and `AddPreset(Preset)` forwarders (the latter passes
  `CameraPosition`), and `PresetOf(Guid)`.

Tests derive world positions of the added track's points by hand from a preset with known anchor
yaw and a known ground point.

## Wave 2 (after A and B merge)

### C. The folder and the library

`src/Vista.Core/Scenes/SceneFolder.cs` (§1, §2.4): `RootFor(parent)`, `Exists`, `Create()`,
`SceneNames()` (readable scenes only, sorted ignoring case; unreadable ones reported through an
`Action<string, Exception>? unreadable` passed to the constructor), `LoadScene`, `SaveScene` (write
`<name>.json.tmp` then `File.Move(..., overwrite: true)`), `RenameScene` (a case-only rename goes
through a temporary name), `DeleteScene`, and the same four for presets (`PresetNames`,
`LoadPreset` setting the track name from the file, `SavePreset`, `DeletePreset`). Tests use a
temporary directory per test.

`src/Vista.Core/Scenes/SceneLibrary.cs` (§1.2, §2.3–§2.5): wraps a `SceneFolder` and a
`SessionState`; `CurrentName`; `Open(string? last)`; `Switch(name)`; `New(name)`; `Rename(name)`;
`Duplicate(name)`; `Delete()`; `SaveNow()`; `Tick(double now)`; each returning a refusal or null,
and an `IOException` from the folder becomes a refusal starting "Could not save". Opening after a
delete or with no scenes follows §2.3. Tests use a temporary directory.

## Wave 3 (sequential, in the plugin)

### D. Settings, Setup and the gear

`Configuration` gains `SaveFolder` and `LastScene` (§4). `SetupWindow` (§1.1). `Plugin` builds the
library when a folder is set and exists, opens `LastScene`, ticks the library on `Framework.Update`
with the frame time, saves on unload, stores `LastScene` after library calls, and opens Setup in place
of the Vista window when the folder is missing or a save is refused. Changing folder per §1.2. The
gear button on the top row; update `RightAlign`, `CameraToolsStart` and `TopRowWidth` together.

### E. The selector and preset menus

`HierarchyPanel`: the selector (§2.5), the name prompt and delete confirmation, **Add track**'s
menu with **From preset** and right-click **Delete** (§3.2, §3.3), and **Save as preset** on the
track menu (§3.1).

## Wave 4

### F. Docs and checklist

`BRAINSPLAT.md` fixes (§6). Guide pages **Saving and scenes** and **Presets** to `GUIDES.md`'s rules,
added to `index.md`. Checklist `scripts/checks/saving-and-presets.json` (local, not committed) for
what tests can't pin.
