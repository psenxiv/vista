# UI Polish (Phase 3.e.1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One frameless visual style for the Vista window, plus anchors on the ground, a quieter track row click, a playlist loop, a Hide UI toggle and a LIVE indicator.

**Architecture:** Core gains the playlist loop (scene flag, playback wrap) and a ground reader in place of the foot-height reader. The Plugin gets a collision-ray ground reader, a restyled `IconButton` with toggle and row-action variants, a UI palette, and rearranged top bar, track row, Scene panel and Playlist panel.

**Tech Stack:** C# / .NET 10, Dalamud 15.0.3.5, Dalamud.Bindings.ImGui, FFXIVClientStructs, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-ui-polish-design.md`

## Global Constraints

- `Vista.Core` never references Dalamud or FFXIVClientStructs and never uses `unsafe`.
- Doc comments are one line; inline comments rare (CLAUDE.md).
- Build with `./build.sh` only, never bare `dotnet build`. Test with `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. Both in the foreground.
- Commits: one line, conventional lowercase prefix, no body, no trailer.
- Keys are named in words in user-facing text ("Backtick").
- Tooltips named in the spec are verbatim: "Fly speed", "Hide game UI in Live", "Select aim", "Select direction", "Add track", "Add to playlist", "Loop playlist", "Repeat Count".
- Every icon button's tooltip shows even while disabled (`ImGuiHoveredFlags.AllowWhenDisabled`).
- No backwards compatibility: nothing is saved yet, so no migration.

## Order and parallelism

- Wave 1: Task 1 ∥ Task 2 (worktrees). Both touch `SessionState.cs` in different members, and `CameraSession.cs` in different members.
- Wave 2: Task 3 (the style; touches every UI file).
- Wave 3: Task 4 ∥ Task 5 (worktrees). Task 4 owns `TrackEditorWindow.cs`; Task 5 owns `HierarchyPanel.cs` and `PlaylistPanel.cs`. Both add members to `CameraSession.cs` in different places.

---

### Task 1: The playlist loop (Core)

**Files:**
- Modify: `src/Vista.Core/Scenes/Scene.cs`, `src/Vista.Core/Scenes/PlaylistEditing.cs`, `src/Vista.Core/Tracks/Shot.cs`, `src/Vista.Core/Tracks/PlaylistPlayback.cs`, `src/Vista.Core/Tracks/Director.cs`, `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`
- Test: `tests/Vista.Tests/Tracks/PlaylistPlaybackTests.cs`, `tests/Vista.Tests/Scenes/PlaylistEditingTests.cs`, `tests/Vista.Tests/Session/SessionPlaylistTests.cs`

**Interfaces:**
- Produces: `Scene.PlaylistLoops` (`bool`, default `false`, last positional parameter); `PlaylistEditing.SetPlaylistLoops(Scene, bool) : Scene`; `PlaylistShot(IReadOnlyList<PlaylistItem> Items, bool Loops = false)`; `PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false)`; `SessionState.SetPlaylistLoops(bool) : string?`; `CameraSession.SetPlaylistLoops(bool) : string?`.

- [ ] **Step 1: Failing playback tests**

Add to `PlaylistPlaybackTests.cs`:

```csharp
    [Fact]
    public void ALoopingPlaylistWrapsToTheFirstEntryCarryingTimeOver()
    {
        var items = new[] { Item(Ten()), Item(Ten()) };
        var playback = new PlaylistPlayback(items, loops: true);

        playback.Advance(23f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALoopingPlaylistWrapsAtMostOncePerAdvance()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())], loops: true);

        playback.Advance(45f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void AnEntryThatHoldsThePlaylistStillHoldsWhenItLoops()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten(loop: true))], loops: true);

        playback.Advance(100f);

        Assert.Equal(1, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALoopingPlaylistOfZeroLengthEntriesShowsOneEntryPerAdvance()
    {
        var playback = new PlaylistPlayback([Item(Snap(0f, 0f)), Item(Snap(5f, 0f))], loops: true);

        playback.Advance(0.1f);
        var first = playback.Index;
        playback.Advance(0.1f);
        var second = playback.Index;
        playback.Advance(0.1f);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Equal(0, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingInTheLastEntryOfALoopingPlaylistNeverFinishesIt()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())], loops: true);
        playback.Advance(12f);

        playback.Seek(10.0);

        Assert.False(playback.IsFinished);
    }
```

The existing `TheLastFrameHoldsAtTheEnd` already pins "off still holds at the end".

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter PlaylistPlaybackTests`
Expected: build error, `PlaylistPlayback` has no `loops` parameter.

- [ ] **Step 3: The playback wrap**

In `PlaylistPlayback.cs`:

- Doc comment: `/// <summary>Plays playlist entries in turn with a cut between them, carrying time over; at the end holds the last frame, or wraps to the first entry when looping.</summary>`
- Add `private readonly bool loops;` and change the constructor:

```csharp
    /// <summary>Plays <paramref name="items"/> from the first, wrapping at the end when <paramref name="loops"/>; refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false)
    {
        if (items.Count == 0) throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        this.loops = loops;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
    }
```

- Replace the `while` loop in `Advance` with:

```csharp
        var wrapped = false;
        while (!IsFinished && clock >= Total && (Total > 0 || clock > 0))
        {
            if (Index == items.Count - 1)
            {
                if (!loops)
                {
                    clock = Total;
                    IsFinished = true;
                    break;
                }

                // One wrap per Advance, so a very long frame or a playlist with no length never spins.
                if (wrapped)
                {
                    Index = 0;
                    clock = 0;
                    break;
                }

                wrapped = true;
                clock -= Total;
                Index = 0;
            }
            else
            {
                clock -= Total;
                Index++;
            }

            // A zero-length entry is shown for the frame it's reached on.
            if (Total == 0)
            {
                clock = 0;
                break;
            }
        }
```

- In `Seek`, the last line becomes `IsFinished = !loops && Index == items.Count - 1 && clock >= Total;`

Trace for `ALoopingPlaylistWrapsAtMostOncePerAdvance`: clock 45 at entry 0 (Total 10) → 35, Index 1 → 35 ≥ 10 at the last entry: first wrap, clock 25, Index 0 → 15, Index 1 → 15 ≥ 10 at the last entry with `wrapped` set: Index 0, clock 0. Unlimited wrapping would land at entry 0, 5 s; the guard lands at 0 s. Passes.

Trace for `ALoopingPlaylistOfZeroLengthEntriesShowsOneEntryPerAdvance`: the first Advance shows entry 0's first frame (the `shownFirstFrame` branch, Total 0). Second: clock 0.1 at Index 0, `clock -= Total` leaves 0.1, Index 1, Total 0 → clock 0, break: Index 1. Third: clock 0.1 at the last entry, loops, first wrap: clock 0.1, Index 0, Total 0 → clock 0, break: Index 0. Passes.

`AnEntryThatHoldsThePlaylistStillHoldsWhenItLoops`: entry 1's Total is infinite, so the loop stops there. Passes.

- [ ] **Step 4: The shot and the Director**

`Shot.cs`:

```csharp
/// <summary>Plays playlist entries in turn, wrapping to the first at the end when <paramref name="Loops"/>.</summary>
public sealed record PlaylistShot(IReadOnlyList<PlaylistItem> Items, bool Loops = false) : Shot;
```

Keep the existing doc comment's wording if it is already close; the parameter is what matters. `Director.cs:35`: `PlaylistShot p => new PlaylistPlayback(p.Items, p.Loops),`

- [ ] **Step 5: Run the playback tests**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter PlaylistPlaybackTests`
Expected: PASS.

- [ ] **Step 6: Failing scene and session tests**

`PlaylistEditingTests.cs` (use the file's existing scene helper; if it has none, build a scene with `SceneEditing` as its other tests do):

```csharp
    [Fact]
    public void SetPlaylistLoopsSetsTheFlagAndKeepsTheSceneWhenUnchanged()
    {
        var scene = /* the file's usual one-track scene */;

        var looping = PlaylistEditing.SetPlaylistLoops(scene, true);

        Assert.True(looping.PlaylistLoops);
        Assert.Same(looping, PlaylistEditing.SetPlaylistLoops(looping, true));
    }
```

`SessionPlaylistTests.cs` (use its existing `Editing()`-style helper that gives an edited track with points):

```csharp
    [Fact]
    public void SettingThePlaylistLoopIsOneUndoStep()
    {
        var state = /* editing, one track with points */;

        Assert.Null(state.SetPlaylistLoops(true));
        Assert.True(state.Scene.PlaylistLoops);

        state.Undo();
        Assert.False(state.Scene.PlaylistLoops);
    }

    [Fact]
    public void SettingThePlaylistLoopIsRefusedUnlessEditing()
    {
        var state = /* editing, one track with points */;
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();

        Assert.NotNull(state.SetPlaylistLoops(true));
        Assert.False(state.Scene.PlaylistLoops);
    }

    [Fact]
    public void LivePlaysALoopingPlaylistRoundAgain()
    {
        var state = /* editing, one track with points */;
        state.AddToPlaylist(state.EditedTrackId);
        state.SetPlaylistLoops(true);
        state.Play(); // Edit previews; go live the way the file's other live tests do (Cue then Play)

        state.Director.Advance((float)(state.Duration + 1.0));

        Assert.False(state.Director.IsFinished);
        Assert.Equal(1.0, state.Director.ShotTime, 4);
    }
```

Match how the file already goes live and advances (its other tests show the calls; for example `state.Cue(); state.Play();` and whatever advance method they use). Adjust member names to the real ones, keeping each test's intent.

- [ ] **Step 7: Run and see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: build errors for `PlaylistLoops`, `SetPlaylistLoops`.

- [ ] **Step 8: Scene, edit, session, wrapper**

`Scene.cs`:

```csharp
/// <summary>The tracks being worked on, in Hierarchy order, which are hidden, the playlist Live plays and whether it loops, and the anchor they hang off.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, IReadOnlyList<PlaylistEntry> Playlist, Anchor Anchor = default, bool AnchorPlaced = false, bool PlaylistLoops = false);
```

`PlaylistEditing.cs`, after `SetLoops`:

```csharp
    /// <summary>Sets whether Live wraps from the last entry to the first.</summary>
    public static Scene SetPlaylistLoops(Scene scene, bool loops) => scene.PlaylistLoops == loops ? scene : scene with { PlaylistLoops = loops };
```

`SessionState.cs`:
- `SameValues`: add `|| a.PlaylistLoops != b.PlaylistLoops` to the first `if`, and mention the loop in its doc comment.
- After `SetEntryLoops`:

```csharp
    /// <summary>Sets whether Live loops the playlist, as one undo step. Returns why it was refused, or null.</summary>
    public string? SetPlaylistLoops(bool loops) => CommitScene(scene => (PlaylistEditing.SetPlaylistLoops(scene, loops), EditedTrackId));
```

- `GoLive`: `Director.GoLive(new PlaylistShot(items, Scene.PlaylistLoops));`

`CameraSession.cs`, after `SetEntryLoops`:

```csharp
    /// <summary>Sets whether Live loops the playlist. Returns why it was refused, or null.</summary>
    public string? SetPlaylistLoops(bool loops) => state.SetPlaylistLoops(loops);
```

- [ ] **Step 9: Run everything**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj` then `./build.sh`
Expected: all pass; 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(tracks) loop the playlist"
```

---

### Task 2: Anchors on the ground, and no Bring scene to me

**Files:**
- Create: `src/Vista.Plugin/Game/Ground.cs`
- Modify: `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`, `src/Vista.Plugin/Ui/HierarchyPanel.cs`
- Test: `tests/Vista.Tests/Session/SessionAnchorTests.cs`, `tests/Vista.Tests/Session/SessionPreviewTests.cs`

**Interfaces:**
- Produces: `SessionState(Func<Vector3, float?>? groundBelow = null)`; `Ground.Below(Vector3 point) : float?` (Plugin). Removes `SessionState.BringScene` and `CameraSession.BringSceneToMe`.

- [ ] **Step 1: Failing tests**

In `SessionAnchorTests.cs`:
- Every `new SessionState(() => 1f)` becomes `new SessionState(_ => 1f)`, and the `Editing()` helper's comment reads "Editing with the ground at y = 1".
- Rename `TheFirstPointPlacesBothAnchorsUnderItAtFootHeight` to `TheFirstPointPlacesBothAnchorsOnTheGroundUnderIt`.
- Delete `BringSceneMovesTheSceneAnchorToTheCameraAtFootHeightKeepingItsYaw` and `BringSceneIsRefusedUntilTheSceneAnchorIsPlaced`, and the `BringScene` assertion near line 247 (keep the rest of that test if it tests something else; delete the test if `BringScene` was its point).
- Add:

```csharp
    [Fact]
    public void TheGroundIsReadUnderTheFirstPoint()
    {
        Vector3? asked = null;
        var state = new SessionState(p => { asked = p; return 2f; });
        state.Edit();

        state.AddToEnd(Point(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), asked);
        Assert.Equal(2f, state.Scene.Anchor.Position.Y);
        Assert.Equal(2f, state.Scene.Tracks[0].Anchor.Position.Y);
    }

    [Fact]
    public void WithNoGroundTheAnchorsSitAtThePointsHeight()
    {
        var state = new SessionState(_ => null);
        state.Edit();

        state.AddToEnd(Point(10f));

        Assert.Equal(5f, state.Scene.Anchor.Position.Y);
    }
```

(`Point(x)` in this file puts points at y = 5; check and adjust the expected vectors if not. If the track anchor's position is stored relative to the scene anchor, assert its world position the way the file's other tests do.)

In `SessionPreviewTests.cs:181`, the test using `BringScene` checks that an anchor edit stops the preview. Replace the `BringScene` call with another scene-anchor edit the file already uses (for example a `MoveAnchor`/`EndLiveEdit` pair, or `SelectSceneAnchor` plus the anchor move call the other anchor tests use), keeping the assertion that the preview stops.

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: build errors (`Func<float?>` vs a lambda with a parameter).

- [ ] **Step 3: The ground reader in Core**

`SessionState.cs`:

```csharp
    private readonly Func<Vector3, float?> groundBelow;
```

```csharp
    /// <summary>A session; <paramref name="groundBelow"/> finds the ground's height under a world point, or null when it can't.</summary>
    public SessionState(Func<Vector3, float?>? groundBelow = null)
    {
        this.groundBelow = groundBelow ?? (_ => null);
```

In `WithPoint`: `SceneGeometry.PlaceFor(scene, EditedTrackId, world.Position, groundBelow(world.Position) ?? world.Position.Y)`.

Delete `BringScene` and its doc comment. If `UnplacedRefusal` or anything else becomes unused, the compiler warns: delete what's unused.

`SceneGeometry.PlaceFor`'s parameter is `footHeight`: rename it `groundHeight` and its doc comment to "…under a first point at ground height…".

- [ ] **Step 4: The ground reader in the Plugin**

`src/Vista.Plugin/Game/Ground.cs`:

```csharp
using System.Numerics;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;

namespace Vista.Plugin.Game;

/// <summary>Finds the ground under a world point: the first collision hit straight down, else the character's feet.</summary>
internal static class Ground
{
    private const float MaxDrop = 1000f;

    /// <summary>The ground's height under <paramref name="point"/>, the character's foot height when nothing is hit, or null.</summary>
    public static float? Below(Vector3 point)
    {
        if (BGCollisionModule.RaycastMaterialFilter(point, -Vector3.UnitY, out var hit, MaxDrop)) return hit.Point.Y;
        return Plugin.ObjectTable.LocalPlayer?.Position.Y;
    }
}
```

Check the namespace of `BGCollisionModule` in the read-only clone `~/code/FFXIVClientStructs` (`FFXIVClientStructs/FFXIV/Common/Component/BGCollision/BGCollisionModule.cs`) and in the bundled assembly (it builds against `$DALAMUD_HOME/FFXIVClientStructs.dll`). The static helper `RaycastMaterialFilter(Vector3, Vector3, out RaycastHit, float)` exists there. It must run on the framework thread; adding points already does.

`CameraSession.cs:12`: `private readonly SessionState state = new(Ground.Below);` (add `using Vista.Plugin.Game;` if missing). Delete `BringSceneToMe`.

`HierarchyPanel.cs`: delete the Bring scene to me button and fix the `buttons` width to the anchor button alone.

- [ ] **Step 5: Run everything**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj` then `./build.sh`
Expected: all pass; 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(scenes) place anchors on the ground and drop bring scene to me"
```

---

### Task 3: One style

**Files:**
- Create: `src/Vista.Plugin/Ui/UiColours.cs`
- Modify: `src/Vista.Plugin/Ui/IconButton.cs`, `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Plugin/Ui/HierarchyPanel.cs`, `src/Vista.Plugin/Ui/PlaylistPanel.cs`, `src/Vista.Plugin/Ui/PointWindow.cs`, `src/Vista.Plugin/Ui/TimingWindow.cs` (only where they draw icon buttons)

**Interfaces:**
- Produces:
  - `UiColours.Accent`, `UiColours.Amber`, `UiColours.Red`, `UiColours.Dim()` (`uint`; `Dim()` is a method because it reads the style's text colour at 40% alpha), `UiColours.Muted()` (text at 60% alpha).
  - `IconButton.Draw(string id, FontAwesomeIcon icon, string tooltip, uint? iconColour = null, bool danger = false) : bool` — frameless; `danger` turns the icon red while hovered.
  - `IconButton.Toggle(string id, FontAwesomeIcon icon, bool on, string tooltip) : bool` — accent when on, dimmed when off.
  - `IconButton.RowAction(string id, FontAwesomeIcon icon, string tooltip, bool rowHovered, bool danger = false) : bool` — draws only when `rowHovered`, otherwise reserves its space with `ImGui.Dummy` and returns false.
  - `IconButton.Width(FontAwesomeIcon)` unchanged.
  - `IconButton.RowHovered(Vector2 min, Vector2 max) : bool` — the mouse is over that rectangle and the current window (or a child) is hovered.

No Core changes, no tests. The build and the checklist cover it.

- [ ] **Step 1: The palette**

```csharp
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui;

/// <summary>The Vista window's colours, as ImGui ABGR.</summary>
internal static class UiColours
{
    /// <summary>Selection and toggles that are on.</summary>
    public const uint Accent = 0xFFF0A040;

    /// <summary>Loop counts.</summary>
    public const uint Amber = 0xFF40C0FF;

    /// <summary>LIVE and destructive hover.</summary>
    public const uint Red = 0xFF4050E8;

    /// <summary>Off and greyed-out icons: the text colour at 40%.</summary>
    public static uint Dim() => ImGui.GetColorU32(ImGuiCol.Text, 0.4f);

    /// <summary>Section headers: the text colour at 60%.</summary>
    public static uint Muted() => ImGui.GetColorU32(ImGuiCol.Text, 0.6f);
}
```

`PlaylistPanel`'s private `Amber` constant goes; use `UiColours.Amber`. `EditorColours` (the in-world overlay) stays as it is.

- [ ] **Step 2: Frameless icon buttons**

`IconButton.cs`:

```csharp
    // The danger button hovered last frame, so its icon can be red while hovered.
    private static uint? dangerHovered;

    /// <summary>A frameless icon button with a tooltip, shown even while disabled; <paramref name="danger"/> turns the icon red on hover.</summary>
    public static bool Draw(string id, FontAwesomeIcon icon, string tooltip, uint? iconColour = null, bool danger = false)
    {
        var red = danger && dangerHovered == ImGui.GetID(id);
        var colour = red ? UiColours.Red : iconColour;
        bool pressed;
        using (Frameless())
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            pressed = ImGuiComponents.IconButton(id, icon);
        if (danger)
        {
            if (ImGui.IsItemHovered()) dangerHovered = ImGui.GetID(id);
            else if (red) dangerHovered = null;
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return pressed;
    }
```

`Frameless()` pushes `ImGuiCol.Button` transparent (`0u`), `ImGuiCol.ButtonHovered` at `ImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.35f)`, `ImGuiCol.ButtonActive` at `ImGui.GetColorU32(ImGuiCol.ButtonActive, 0.5f)`, and `ImGuiStyleVar.FrameRounding` 4f, returning one disposable (check `~/code/Dalamud/Dalamud/Interface/Utility/Raii/` for how `ImRaii.Color`/`ImRaii.Style` chain `Push` calls). One frame of lag on the red hover is invisible. `ImGui.GetID(id)` must be read in the same ID scope as the button, as above.

```csharp
    /// <summary>A toggle: the icon in the accent colour when on, dimmed when off.</summary>
    public static bool Toggle(string id, FontAwesomeIcon icon, bool on, string tooltip)
        => Draw(id, icon, tooltip, on ? UiColours.Accent : UiColours.Dim());

    /// <summary>A row's action, drawn only while the row is hovered; otherwise its space stays empty.</summary>
    public static bool RowAction(string id, FontAwesomeIcon icon, string tooltip, bool rowHovered, bool danger = false)
    {
        if (rowHovered) return Draw(id, icon, tooltip, danger: danger);
        ImGui.Dummy(new Vector2(Width(icon), ImGui.GetFrameHeight()));
        return false;
    }

    /// <summary>True when the mouse is over the rectangle and the window or one of its children is hovered.</summary>
    public static bool RowHovered(Vector2 min, Vector2 max)
        => ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) && ImGui.IsMouseHoveringRect(min, max, false);
```

If `ImGuiComponents.IconButton` draws its own background colours regardless of the pushes, draw the button directly instead: `ImGui.PushFont(UiBuilder.IconFont)`, `ImGui.Button($"{icon.ToIconString()}##{id}")`, pop the font. `Width` stays correct either way. Record which you used in the report.

- [ ] **Step 3: Use toggles and danger everywhere**

- `TrackEditorWindow`: the Hierarchy and Playlist toggles become `IconButton.Toggle(..., showHierarchy, ...)` and `IconButton.Toggle(..., showPlaylist, ...)`; `DrawLoop` becomes `IconButton.Toggle("loop", FontAwesomeIcon.Repeat, loop, loop ? "Play once" : "Loop")`; a pinned pin draws with `iconColour: UiColours.Accent`. Clear track passes `danger: true`.
- Point rows: measure the row with the row `Selectable` (it spans all columns): after it, `var rowMin = ImGui.GetItemRectMin(); var rowMax = ImGui.GetItemRectMax();` and `var rowHovered = IconButton.RowHovered(rowMin, rowMax);`. The trash becomes `IconButton.RowAction($"delete{index}", FontAwesomeIcon.Trash, "Delete point", rowHovered, danger: true)`. An unpinned pin becomes a `RowAction` (dimmed colour is dropped, since it only shows on hover); a pinned pin is always drawn.
- `HierarchyPanel`: the eye and anchor buttons stay `Draw` (they carry state and are frameless now). A hidden track's eye (`EyeSlash`) uses `UiColours.Dim()`.
- `PlaylistPanel`: the remove button becomes `RowAction(..., danger: true)`, measuring the row from the start of the name `Selectable` to the child's right edge: `new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, ImGui.GetItemRectMax().Y)`.
- `PointWindow` and `TimingWindow`: any icon button that deletes gets `danger: true`; nothing else changes.

- [ ] **Step 4: Muted headers**

`HierarchyPanel`'s "Scene" and `PlaylistPanel`'s "Playlist" (not the live "2 / 3 — Crane" line) draw with `using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))`.

- [ ] **Step 5: Build**

Run: `./build.sh` then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: 0 warnings, 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Plugin/Ui
git commit -m "feat(ui) frameless icon buttons, hover row actions and one palette"
```

---

### Task 4: The top bar and the track row

**Files:**
- Modify: `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Plugin/Session/CameraSession.cs`

**Interfaces:**
- Consumes: `IconButton.Draw/Toggle`, `UiColours` (Task 3).
- Produces: `CameraSession.HideUiInLive { get; set; }` (`bool`, default false).

No Core changes; the build and the checklist cover it.

- [ ] **Step 1: The Hide UI setting**

In `CameraSession.cs`:

```csharp
    private bool hideUiInLive;

    /// <summary>Whether Live hides the game UI while it plays; toggling it while Live plays applies at once.</summary>
    public bool HideUiInLive
    {
        get => hideUiInLive;
        set
        {
            if (hideUiInLive == value) return;
            hideUiInLive = value;
            if (state.Mode != CameraMode.Live) return;
            if (value && !state.Director.IsPaused) GameUi.Hide();
            else if (!value) GameUi.Restore();
        }
    }
```

In `Apply`, every `GameUi.Hide()` (the `ReHid` and `Resumed` cases and the final one) becomes `if (hideUiInLive) GameUi.Hide();`. `GameUi.Restore()` calls elsewhere stay; they only act when Vista hid the UI.

A cued Live that hasn't played has `IsPaused` true, so turning the setting on then doesn't hide the UI, as the spec says. Check that a cued Director reports `IsPaused` (it goes live paused); if it reports something else, use that.

- [ ] **Step 2: The top bar**

`DrawTopRow` becomes, left to right: Hierarchy toggle, Playlist toggle, mode combo, LIVE, (gap) Undo, Redo, Timing, (gap) fly speed while editing, then Hide UI right-aligned. `DrawAddButton` leaves the top row.

LIVE, after the mode combo:

```csharp
        if (session.Mode == CameraMode.Live)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            var pulse = 0.55f + (0.45f * MathF.Cos((float)ImGui.GetTime() * MathF.PI));
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red))
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * pulse))
                ImGui.TextUnformatted("LIVE");
        }
```

(`cos(t·π)` has a 2-second period.)

Fly speed, after Timing, only while editing:

```csharp
    /// <summary>The free-cam's speed: a feather icon and a short slider showing the multiplier.</summary>
    private void DrawFlySpeed()
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.Feather.ToIconString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly speed");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(SpeedWidth);
        var speed = session.Speed;
        var step = speed.Index;
        if (ImGui.SliderInt("##speed", ref step, 0, FlySpeed.Steps.Count - 1, $"{speed.Multiplier:0.##}x")) speed.Set(step);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly speed");
    }
```

Remove the "Speed" label and slider from `DrawScrubRow`, and its `speedWidth` reservation: the scrub slider takes the full remaining width.

Hide UI, last on the row:

```csharp
        ImGui.SameLine();
        RightAlign(IconButton.Width(FontAwesomeIcon.EyeSlash));
        if (IconButton.Toggle("hide-ui", FontAwesomeIcon.EyeSlash, session.HideUiInLive, "Hide game UI in Live"))
            session.HideUiInLive = !session.HideUiInLive;
```

- [ ] **Step 3: The track row**

`DrawTrackRow`, left to right: Aim, Direction, Loop, Speed, Duration, + Add, Clear right-aligned.

Aim and Direction become icon buttons opening popups with the same `Selectable` items as the combos had:

```csharp
    private static readonly FontAwesomeIcon[] DirectionIcons = [FontAwesomeIcon.ArrowRight, FontAwesomeIcon.ArrowLeft, FontAwesomeIcon.ArrowsAltH];
```

```csharp
        var aim = session.Track.Aim == AimMode.AimKeys ? 0 : 1;
        if (IconButton.Draw("aim", FontAwesomeIcon.Crosshairs, $"Select aim ({AimNames[aim]})")) ImGui.OpenPopup("aim-menu");
        if (ImGui.BeginPopup("aim-menu"))
        {
            for (var i = 0; i < AimNames.Length; i++)
            {
                if (!ImGui.Selectable(AimNames[i], i == aim) || i == aim) continue;
                var mode = i == 0 ? AimMode.AimKeys : AimMode.PathTangent;
                Report(session.ChangeTrack(t => t with { Aim = mode }));
            }

            ImGui.EndPopup();
        }

        var direction = Array.IndexOf(Directions, session.Track.Direction);
        ImGui.SameLine();
        if (IconButton.Draw("direction", DirectionIcons[direction], $"Select direction ({DirectionNames[direction]})")) ImGui.OpenPopup("direction-menu");
        if (ImGui.BeginPopup("direction-menu"))
        {
            for (var i = 0; i < DirectionNames.Length; i++)
            {
                if (!ImGui.Selectable(DirectionNames[i], i == direction) || i == direction) continue;
                var chosen = Directions[i];
                Report(session.ChangeTrack(t => TrackEditing.SetDirection(t, chosen)));
            }

            ImGui.EndPopup();
        }
```

Speed and Duration: `LabelledField` takes a `FontAwesomeIcon` in place of the label text and draws it in the icon font, with the field's tooltip on both the icon and the field. Speed uses `FontAwesomeIcon.TachometerAlt`; Duration uses `FontAwesomeIcon.Stopwatch` with the format `"%.1f s"`. Check `PendingField` still parses a value typed as "5" or "5 s"; if a suffix in the format breaks typing (as it did for the old loop cell), keep `"%.1f"` for the field and draw a small "s" after it instead, and note it in the report.

+ Add, after Duration:

```csharp
    /// <summary>A plus icon that appends a point, and a caret opening the insert menu.</summary>
    private void DrawAddButton()
    {
        if (IconButton.Draw("add-point", FontAwesomeIcon.Plus, "Add point (Backtick)")) Report(session.AddToEnd());
        ImGui.SameLine(0f, 0f);
        if (IconButton.Draw("add-menu", FontAwesomeIcon.CaretDown, "More ways to add")) ImGui.OpenPopup("add-menu");
        if (!ImGui.BeginPopup("add-menu")) return;
        // …the three MenuItems as now…
        ImGui.EndPopup();
    }
```

`AimWidth` and `DirectionWidth` go. `TrackRowWidth()` sums the new items: the Aim, Direction, Loop, two field icons, two `FieldWidth` fields, Plus, CaretDown and Trash widths, plus the gaps between them (count them as you lay the row out) and the window padding.

- [ ] **Step 4: Transport buttons**

Play/Pause and Restart already use `IconButton.Draw`, so they are frameless after Task 3. Nothing else changes in the scrub row beyond Step 2.

- [ ] **Step 5: Build**

Run: `./build.sh` then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: 0 warnings, 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Plugin
git commit -m "feat(ui) condense the top bar and track row, add live and hide ui"
```

---

### Task 5: The Scene and Playlist panels

**Files:**
- Modify: `src/Vista.Plugin/Ui/HierarchyPanel.cs`, `src/Vista.Plugin/Ui/PlaylistPanel.cs`, `src/Vista.Plugin/Session/CameraSession.cs`, and `src/Vista.Core/Scenes/SceneGeometry.cs` plus its tests only if `ViewOf` becomes unused

**Interfaces:**
- Consumes: `IconButton.Draw/Toggle/RowAction/RowHovered`, `UiColours` (Task 3); `CameraSession.SetPlaylistLoops`, `Scene.PlaylistLoops` (Task 1).
- Produces: `CameraSession.SelectTrack(Guid id) : string?`, `CameraSession.FlyToFirstPoint(Guid id) : string?`. Removes `CameraSession.OpenTrack`.

- [ ] **Step 1: Track row clicks**

`CameraSession.cs`, replacing `OpenTrack`:

```csharp
    /// <summary>Makes a track the edited one, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectTrack(Guid id) => state.SwitchTrack(id);

    /// <summary>Edits a track and flies the editor camera to its first point, as a point's double-click does. Returns why it was refused, or null.</summary>
    public string? FlyToFirstPoint(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is null) JumpToPoint(0);
        return refusal;
    }
```

`JumpToPoint` already does nothing for a track with no points. If `SceneGeometry.ViewOf` is now unused in `src/`, delete it and its tests in `SceneGeometryTests.cs`.

`HierarchyPanel.DrawRow`: the name `Selectable` calls `session.SelectTrack(track.Id)`; the double-click line becomes `if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Report(session.FlyToFirstPoint(track.Id));`. Rename stays in the right-click menu only. Update `DrawRow`'s doc comment: "click edits the track, double-click flies to its first point, right-click opens the menu, drag reorders".

- [ ] **Step 2: The Scene header**

"Scene" (muted, from Task 3), then right-aligned the scene anchor button and a plus icon:

```csharp
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + IconButton.Width(FontAwesomeIcon.Plus) + ImGui.GetStyle().ItemSpacing.X;
        // …SameLine and right-align as now…
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor")) Report(session.SelectSceneAnchor());
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (IconButton.Draw("add-track", FontAwesomeIcon.Plus, "Add track")) Report(session.AddTrack());
```

The `tracks` child no longer reserves a footer (`new Vector2(0f, 0f)`), and the "+ Track" button goes.

- [ ] **Step 3: The Playlist header**

The header line keeps "Playlist" or the live "2 / 3 — Crane", then right-aligned, disabled unless editing:

```csharp
        if (IconButton.Toggle("playlist-loop", FontAwesomeIcon.Repeat, scene.PlaylistLoops, "Loop playlist"))
            Report(session.SetPlaylistLoops(!scene.PlaylistLoops));
        ImGui.SameLine();
        if (IconButton.Draw("add-entry", FontAwesomeIcon.Plus, "Add to playlist")) ImGui.OpenPopup("add-entry");
```

Move the `add-entry` popup up with it. The `entries` child no longer reserves a footer, and the footer "+ Add" goes. The loop toggle's tooltip shows while disabled (it's an `IconButton`).

- [ ] **Step 4: The loop cell, click to type**

Replace `DrawLoops` and `pendingLoops` with a click-to-type cell, following `HierarchyPanel`'s rename pattern:

```csharp
    private Guid? editingLoops;
    private string loopsText = string.Empty;
    private bool focusLoops;
```

```csharp
    /// <summary>The repeat count: a number, ∞ when the entry holds the playlist, or — when it plays once; click to type, wheel to step.</summary>
    private void DrawLoops(Scene scene, PlaylistEntry entry, bool editing)
    {
        if (editingLoops == entry.Id)
        {
            DrawLoopsInput(entry);
            return;
        }

        var holds = PlaylistEditing.HoldsPlaylist(scene, entry);
        var text = entry.Loops is { } n ? n.ToString() : holds ? "∞" : "—";
        var colour = entry.Loops is not null || holds ? UiColours.Amber : UiColours.Dim();
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
            if (ImGui.Selectable($"{text}##loops", false, ImGuiSelectableFlags.None, new Vector2(LoopWidth, ImGui.GetFrameHeight())))
                StartLoops(entry);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Repeat Count");
        if (editing && ImGui.IsItemHovered()) StepLoops(entry);
    }

    /// <summary>The number box: Enter or clicking away sets the count, Escape cancels; empty or 0 follows the track.</summary>
    private void DrawLoopsInput(PlaylistEntry entry)
    {
        if (focusLoops)
        {
            ImGui.SetKeyboardFocusHere();
            focusLoops = false;
        }

        ImGui.SetNextItemWidth(LoopWidth);
        var entered = ImGui.InputText("##loops-input", ref loopsText, 3, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            editingLoops = null;
            return;
        }

        if (!entered && !ImGui.IsItemDeactivated()) return;
        var count = int.TryParse(loopsText, out var n) && n > 0 ? n : (int?)null;
        Report(session.SetEntryLoops(entry.Id, count));
        editingLoops = null;
    }

    private void StartLoops(PlaylistEntry entry)
    {
        editingLoops = entry.Id;
        loopsText = entry.Loops?.ToString() ?? string.Empty;
        focusLoops = true;
    }

    /// <summary>The mouse wheel steps the count by one: down from 1 empties it, up from empty gives 1.</summary>
    private void StepLoops(PlaylistEntry entry)
    {
        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f) return;
        ClaimWheel();
        int? next = wheel > 0f
            ? Math.Min((entry.Loops ?? 0) + 1, PlaylistEditing.MaxLoops)
            : entry.Loops is { } n && n > 1 ? n - 1 : null;
        if (next != entry.Loops) Report(session.SetEntryLoops(entry.Id, next));
    }
```

`ClaimWheel()` stops the `entries` child scrolling while the wheel is over a loop cell. Use `ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY)` if the binding has it (check `~/code/Dalamud` and the `Dalamud.Bindings.ImGui` assembly). If not, keep a `bool wheelOverLoops` set here, and open the `entries` child with `ImGuiWindowFlags.NoScrollWithMouse` on the frame after it was set (clear it at the start of `Draw` after reading). Record which you used.

Clear `editingLoops` when not editing (as the rename is cleared) and when the entry disappears. `DrawRow` passes `editing` through, and the loop cell sits inside the row's `BeginDisabled(!editing)` scope already.

- [ ] **Step 5: Build**

Run: `./build.sh` then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: 0 warnings, 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(ui) tidy the scene and playlist panels and type repeat counts"
```

---

### After the tasks: the checklist

The controller writes `CHECKLIST-3e1.md` (untracked) after the final review, with falsifiable pass conditions, a Notes line each, and keys as words.
