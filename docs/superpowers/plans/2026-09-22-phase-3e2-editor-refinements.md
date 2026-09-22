# Editor Refinements (Phase 3.e.2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One Point window layout for points and anchors with bordered, tooltipped fields; tighter wording and placement in the main window; track names in the world; a faster default track speed.

**Architecture:** Plugin-side UI changes in three disjoint file sets, plus one Core constant.

**Tech Stack:** C# / .NET 10, Dalamud 15.0.3.5, Dalamud.Bindings.ImGui, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-editor-refinements-design.md`

## Global Constraints

- `Vista.Core` never references Dalamud or FFXIVClientStructs and never uses `unsafe`.
- Doc comments are one line; inline comments rare (CLAUDE.md).
- Build with `./build.sh` only, never bare `dotnet build`. Test with `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. Both in the foreground.
- Commits: one line, conventional lowercase prefix, no body, no trailer.
- Tooltips are verbatim from the spec: "X", "Y", "Z", "Pitch", "Yaw", "Roll", "FoV", "Position", "Rotation", "Track speed", "Track duration", "Add point", "Hide game UI when Live", "Repeats".
- Every tooltip shows while disabled (`ImGuiHoveredFlags.AllowWhenDisabled`).
- Use `IconButton`, `UiColours` and `EditorColours` as they stand; add no new palette.

## Order and parallelism

All three tasks run in parallel worktrees; their files don't overlap.

---

### Task 1: One Point window layout

**Files:**
- Modify: `src/Vista.Plugin/Ui/PointWindow.cs`

- [ ] **Step 1: One header**

Draw the header once for both: "Gizmo", Move and Rotate radios, then right-aligned copy, paste and delete. For an anchor, wrap copy, paste and delete in `ImGui.BeginDisabled(true)`/`EndDisabled()`. For a point they behave as now.

- [ ] **Step 2: One grid**

Replace the two tables (`point-fields`, 6 columns; `anchor-fields`, 4 columns) with one table `fields` of 4 columns: three fields and a row icon. `DrawAnchor` goes; `Draw` builds the rows from either the point or the anchor.

```csharp
    /// <summary>One number field: bordered in <paramref name="border"/>, named by its tooltip, and dragged live as one undo step.</summary>
    private static bool BorderedField(string id, string name, uint? border, ref float value, float speed, string format)
    {
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(FieldWidth);
        bool changed;
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f, border is not null))
        using (ImRaii.PushColor(ImGuiCol.Border, border ?? 0u, border is not null))
            changed = ImGui.DragFloat($"##{id}", ref value, speed, 0f, 0f, format);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(name);
        return changed;
    }

    /// <summary>The row's icon at its right end, naming the row in its tooltip.</summary>
    private static void RowIcon(FontAwesomeIcon icon, string name)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted(icon.ToIconString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
    }
```

Rows:
- Position: X (`EditorColours.AxisX`), Y (`AxisY`), Z (`AxisZ`), then `RowIcon(FontAwesomeIcon.ArrowsAlt, "Position")`.
- Rotation: Pitch (`AxisX`), Yaw (`AxisY`), Roll (`AxisZ`), then `RowIcon(FontAwesomeIcon.SyncAlt, "Rotation")`. For a point, Pitch and Yaw stay disabled under Direction of travel as now.
- FoV: FoV (no border) in the first column only; no icon.

For a point, each field keeps its current behaviour: `BeginLiveEdit` on activate, `PreviewPoint` with the setter on change, `EndLiveEdit` on deactivate. For an anchor, X, Y, Z and Yaw keep the current anchor behaviour (`PreviewAnchor(..., carry: true)`). For an anchor, Pitch, Roll and FoV are drawn inside `BeginDisabled(true)` with the format `"—"` and a dummy value, and never call the session.

Keep the "Right-align to last frame's field grid" logic, measuring the new table.

Check `FontAwesomeIcon.ArrowsAlt` and `SyncAlt` exist in `~/code/Dalamud`; use the nearest if not, and report it.

- [ ] **Step 3: Build and commit**

Run `./build.sh` then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`: 0 warnings, all pass.

```bash
git add src/Vista.Plugin/Ui/PointWindow.cs
git commit -m "feat(ui) one point window layout with bordered fields and row icons"
```

---

### Task 2: Main window wording, LIVE, grip, open width, default speed

**Files:**
- Modify: `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Core/Tracks/TrackEditing.cs`
- Test: `tests/Vista.Tests/Tracks/TrackEditingTests.cs` (it already asserts `TrackEditing.DefaultSpeed`; check no test assumes 2 as a literal, and fix any that do)

- [ ] **Step 1: Default speed (TDD)**

Add to `TrackEditingTests.cs`:

```csharp
    [Fact]
    public void ANewTrackMovesAtFiveYalmsPerSecond() => Assert.Equal(5f, TrackEditing.Empty().Speed);
```

Run it: FAIL (2). Set `public const float DefaultSpeed = 5f;` in `TrackEditing.cs`. Run the whole suite; fix any test that hard-coded the old speed's timings by building its track with an explicit speed rather than changing its expected numbers, keeping each test's intent. PASS.

- [ ] **Step 2: Tooltips**

- Speed field tooltip: "Track speed". Duration field tooltip: "Track duration".
- The add point plus: "Add point".
- Hide UI: "Hide game UI when Live".

- [ ] **Step 3: LIVE beside the eye**

Remove LIVE from after the mode combo. At the right end, draw LIVE (while Live) then the eye:

```csharp
        var eye = IconButton.Width(FontAwesomeIcon.EyeSlash);
        var live = session.Mode == CameraMode.Live ? ImGui.CalcTextSize("LIVE").X + ImGui.GetStyle().ItemSpacing.X : 0f;
        ImGui.SameLine();
        RightAlign(live + eye);
        if (session.Mode == CameraMode.Live)
        {
            DrawLive();
            ImGui.SameLine();
        }
        // …the eye toggle…
```

`TopRowWidth()` still counts the larger of LIVE and fly speed once; LIVE now sits on the right but the sum is the same.

- [ ] **Step 4: No grip**

In `DrawPointRow`, remove the `GripLines` icon and the `SameLine`/`AlignTextToFramePadding` before it. The row `Selectable` stays the drag source. The `##handle` column goes; fix the table's column count and the header setup to match.

- [ ] **Step 5: Minimum width on open**

```csharp
    /// <summary>Opens at the minimum width, keeping the height.</summary>
    public override void OnOpen() => openAtMinimum = true;
```

In `Draw`, before anything else:

```csharp
        if (openAtMinimum)
        {
            openAtMinimum = false;
            ImGui.SetWindowSize(new Vector2(SizeConstraints!.Value.MinimumSize.X, ImGui.GetWindowSize().Y));
        }
```

`PreDraw` has already set the constraints for this frame. Check `Window.OnOpen` exists and is virtual in `~/code/Dalamud/Dalamud/Interface/Windowing/Window.cs`; if it's named differently, use it. `pendingWidth` stays for the compartment toggles.

- [ ] **Step 6: Build and commit**

Run `./build.sh` then `dotnet test`: 0 warnings, all pass.

```bash
git add src tests
git commit -m "feat(ui) tighten main window wording, move live, drop the grip, open narrow and speed up new tracks"
```

---

### Task 3: Scene rows, Repeats, and names in the world

**Files:**
- Modify: `src/Vista.Plugin/Ui/HierarchyPanel.cs`, `src/Vista.Plugin/Ui/PlaylistPanel.cs`, `src/Vista.Plugin/Editor/Overlay.cs`, `src/Vista.Plugin/Editor/EditorLayer.cs`

- [ ] **Step 1: Track rows**

In `HierarchyPanel.DrawRow`, draw the name first, sized to leave room on the right for the anchor button and the eye (`IconButton.Width(Anchor) + IconButton.Width(Eye) + ItemSpacing.X * 2`), then `SameLine` and the anchor button, then `SameLine` and the eye. The rename field takes the same width. Drag, drop, double-click and the context menu stay attached to the name `Selectable`.

- [ ] **Step 2: Repeats**

`PlaylistPanel`'s loop cell tooltip: "Repeats".

- [ ] **Step 3: Names in the world**

`Overlay.DrawTrackAnchor` gains a `string? name` parameter. When not null, after the ring, project the anchor's position; if it's on screen, draw the name centred horizontally, its bottom just above the ring's top on screen (project `world.Position + Vector3.UnitY * AnchorRadius` or offset by a few pixels above the projected ring), in the same colour as the ring (`selected ? Selected : edited ? Anchor : OtherAnchor`), using `list.AddText`. Follow how `Overlay` already projects points and draws marker numbers (`MarkerText`), and do nothing when the projection is behind the camera.

`EditorLayer` passes the track's name for each drawn track anchor (edited and others). Hidden tracks aren't drawn there already; check, and pass no name for them if they are. The preview already skips the overlay.

- [ ] **Step 4: Build and commit**

Run `./build.sh` then `dotnet test`: 0 warnings, all pass.

```bash
git add src/Vista.Plugin
git commit -m "feat(editor) show track names at their anchors and tidy scene rows"
```

---

### After the tasks: the checklist

The controller writes `CHECKLIST-3e2.md` (untracked) after the final review.
