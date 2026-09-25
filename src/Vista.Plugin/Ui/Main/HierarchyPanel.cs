using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The scene selector and the scene's tracks: pick one to edit, show or hide, rename, duplicate, delete, reorder, and save or add presets.</summary>
internal sealed class HierarchyPanel
{
    private const string NamePopup = "Name###vista-name";
    private const string DeletePopup = "Delete###vista-delete";

    /// <summary>The width of each of a prompt's two buttons.</summary>
    private const float PromptButtonWidth = 127f;

    /// <summary>The name fields' buffer, a little past the longest name so a longer one can be typed and refused.</summary>
    private const int NameBuffer = SceneNames.MaxLength + 8;

    /// <summary>What the name prompt is naming.</summary>
    private enum Naming
    {
        NewScene,
        RenameScene,
        DuplicateScene,
        SavePreset,
    }

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly SceneFiles files;
    private Guid? renaming;
    private string renameText = string.Empty;
    private bool focusRename;

    private IReadOnlyList<string> scenes = [];
    private IReadOnlyList<string> presets = [];
    private Naming? naming;
    private string nameText = string.Empty;
    private (string Text, string? Refusal, bool Replaces)? checkedName;
    private Guid presetTrack;
    private bool openName;
    private bool focusName;
    private (bool Preset, string Name)? deleting;
    private bool openDelete;

    public HierarchyPanel(GameSession game, SceneFiles files)
    {
        this.game = game;
        session = game.State;
        this.files = files;
    }

    /// <summary>The scene selector with its anchor and add buttons, then one row per track; disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.RowWidth(IconButton.Width(FontAwesomeIcon.Anchor), LastSlot());
        ImGui.SetNextItemWidth(
            MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons - ImGui.GetStyle().ItemSpacing.X)
        );
        DrawSelector();
        ImGui.SameLine();
        Layout.RightAlign(buttons);
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor"))
            Report(session.Selection.SelectSceneAnchor());
        ImGui.EndDisabled();
        CentreInLastSlot(FontAwesomeIcon.Plus);
        if (IconButton.Draw("add-track", FontAwesomeIcon.Plus, "Add track"))
            ImGui.OpenPopup("add-track-menu");
        DrawAddMenu();
        ImGui.EndDisabled();
        ImGui.Separator();

        // Rows can delete or reorder tracks, so every row reads this snapshot.
        var scene = session.Scene;
        var edited = session.EditedTrackId;
        if (renaming is { } id && (!editing || SceneEditing.IndexOf(scene, id) < 0))
            renaming = null;
        ImGui.BeginDisabled(!editing);
        if (ImGui.BeginChild("tracks", new Vector2(0f, 0f)))
        {
            var selected = session.Selection.Tracks;
            for (var i = 0; i < scene.Tracks.Count; i++)
                DrawRow(scene, scene.Tracks[i], i, edited, selected, editing);
            DrawSpace(scene, editing);
            DragRows.ScrollNearEdges(DragRows.Track, DragRows.Point);
        }

        ImGui.EndChild();
        ImGui.EndDisabled();

        // Opened here, outside the menus that asked for them, so the popups share one ID scope.
        if (openName)
        {
            ImGui.OpenPopup(NamePopup);
            openName = false;
        }
        if (openDelete)
        {
            ImGui.OpenPopup(DeletePopup);
            openDelete = false;
        }
        DrawNamePrompt();
        DrawDeleteConfirm();
    }

    /// <summary>The open scene's name as a drop-down: every scene to switch to, then New, Rename, Duplicate and Delete; the list is read when it opens.</summary>
    private void DrawSelector()
    {
        var current = files.CurrentName;
        if (!ImGui.BeginCombo("##scene", current.Length > 0 ? current : "Scene"))
            return;
        if (ImGui.IsWindowAppearing())
            scenes = files.Scenes();

        foreach (var name in scenes)
        {
            using var id = ImRaii.PushId(name);
            if (ImGui.Selectable(name, name == current) && name != current)
                Report(files.Switch(name));
        }

        ImGui.Separator();
        if (ImGui.Selectable("New scene"))
            AskName(Naming.NewScene, files.NewSuggestion());
        if (ImGui.Selectable("Rename scene"))
            AskName(Naming.RenameScene, current);
        if (ImGui.Selectable("Duplicate scene"))
            AskName(Naming.DuplicateScene, files.CopySuggestion());
        if (ImGui.Selectable("Delete scene"))
        {
            deleting = (false, current);
            openDelete = true;
        }
        ImGui.Separator();
        if (ImGui.Selectable("Open folder"))
            files.OpenFolder(presets: false);
        ImGui.EndCombo();
    }

    /// <summary>Add track's menu: an empty track, or a preset; right-click a preset to delete it. The presets are read when it opens.</summary>
    private void DrawAddMenu()
    {
        if (!ImGui.BeginPopup("add-track-menu"))
            return;
        if (ImGui.IsWindowAppearing())
            presets = files.PresetNames();

        if (Menu.Item("Empty track"))
            Report(session.AddTrack());
        if (ImGui.BeginMenu("From preset"))
        {
            if (presets.Count == 0)
                Menu.Item("No presets", enabled: false);

            foreach (var name in presets)
            {
                using var id = ImRaii.PushId(name);
                if (Menu.Item(name))
                    Report(files.AddPreset(name));
                if (ImGui.BeginPopupContextItem("preset-menu"))
                {
                    if (Menu.Item("Delete"))
                    {
                        deleting = (true, name);
                        openDelete = true;
                    }
                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();
            if (Menu.Item("Open folder"))
                files.OpenFolder(presets: true);
            ImGui.EndMenu();
        }

        ImGui.EndPopup();
    }

    private void AskName(Naming what, string suggestion)
    {
        naming = what;
        nameText = suggestion;
        checkedName = null;
        openName = true;
        focusName = true;
    }

    /// <summary>The name prompt: Ok stays disabled while the name can't be used and says why; a preset's existing name turns Ok into Replace.</summary>
    private void DrawNamePrompt()
    {
        if (!BeginPrompt(NamePopup, naming is not null) || naming is not { } what)
            return;

        ImGui.TextUnformatted(
            what switch
            {
                Naming.NewScene => "New scene",
                Naming.RenameScene => "Rename scene",
                Naming.DuplicateScene => "Duplicate scene",
                _ => "Save as preset",
            }
        );
        if (focusName)
        {
            ImGui.SetKeyboardFocusHere();
            focusName = false;
        }
        ImGui.SetNextItemWidth(Layout.DialogWidth);
        var entered = ImGui.InputText(
            "##name",
            ref nameText,
            NameBuffer,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll
        );

        // Checked when the text changes, not every frame, since a scene check lists the folder.
        if (checkedName is not { } check || check.Text != nameText)
        {
            var (nameRefusal, nameReplaces) =
                what == Naming.SavePreset
                    ? SceneNames.PresetCheck(nameText, presets)
                    : (files.NameRefusal(nameText, what == Naming.RenameScene), false);
            checkedName = check = (nameText, nameRefusal, nameReplaces);
        }

        var (_, refusal, replaces) = check;
        // Always a line here, blank when the name is fine, so the buttons don't jump as you type.
        using (ImRaii.PushColor(ImGuiCol.Text, refusal is not null ? UiColours.Red : UiColours.Muted()))
            ImGui.TextUnformatted(refusal ?? (replaces ? $"A preset called {nameText.Trim()} exists." : " "));

        ImGui.BeginDisabled(refusal is not null);
        var ok =
            ImGui.Button(replaces ? "Replace" : "Ok", new Vector2(PromptButtonWidth, 0f))
            || (entered && refusal is null);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (Cancelled())
        {
            naming = null;
            ImGui.CloseCurrentPopup();
        }

        if (ok && refusal is null)
        {
            var name = nameText.Trim();
            Report(
                what switch
                {
                    Naming.NewScene => files.New(name),
                    Naming.RenameScene => files.Rename(name),
                    Naming.DuplicateScene => files.Duplicate(name),
                    _ => files.SavePreset(name, presetTrack),
                }
            );
            naming = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>"Delete name? This can't be undone." for a scene or a preset.</summary>
    private void DrawDeleteConfirm()
    {
        if (!BeginPrompt(DeletePopup, deleting is not null) || deleting is not { } target)
            return;

        ImGui.TextUnformatted($"Delete {target.Name}? This can't be undone.");
        if (ImGui.Button("Delete", new Vector2(PromptButtonWidth, 0f)))
        {
            Report(target.Preset ? files.DeletePreset(target.Name) : files.Delete());
            deleting = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (Cancelled())
        {
            deleting = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>Begins prompt <paramref name="popup"/>; true while it's open and has something to ask, otherwise it closes.</summary>
    private static bool BeginPrompt(string popup, bool asking)
    {
        if (!ImGui.BeginPopupModal(popup, ImGuiWindowFlags.AlwaysAutoResize))
            return false;
        if (asking)
            return true;
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return false;
    }

    /// <summary>A prompt's Cancel button; true when it or Escape is pressed.</summary>
    private static bool Cancelled() =>
        ImGui.Button("Cancel", new Vector2(PromptButtonWidth, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape);

    /// <summary>The name, then the anchor button and the eye, shown on hover (a hidden track's eye always): click edits the track, double-click flies to its first point, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, IReadOnlyList<Guid> selected, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);
        // The name's gap before the buttons, then the buttons.
        var buttons =
            ImGui.GetStyle().ItemSpacing.X + IconButton.RowWidth(IconButton.Width(FontAwesomeIcon.Anchor), LastSlot());
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons);
        var rowMin = ImGui.GetCursorScreenPos();
        var rowHovered = editing && IconButton.RowHovered(rowMin, rowMin.Y + ImGui.GetFrameHeight());

        var rowStart = ImGui.GetCursorPosX();
        if (renaming == track.Id)
            DrawRename(track, nameWidth);
        else
            DrawName(scene, track, index, selected, editing, nameWidth);
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + nameWidth + ImGui.GetStyle().ItemSpacing.X);

        var follows = track.Aim == AimMode.FollowTarget;
        ImGui.BeginDisabled(!track.ShowsAnchor);
        var anchorTip = follows ? "Follow Target tracks move with their character" : "Select track anchor";
        if (IconButton.RowAction("anchor", FontAwesomeIcon.Anchor, anchorTip, rowHovered))
            Report(session.Selection.SelectTrackAnchor(track.Id));
        ImGui.EndDisabled();
        var eye = hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye;
        CentreInLastSlot(eye);

        // A hidden track keeps its eye showing, so you can see what's hidden without hovering.
        ImGui.BeginDisabled(isEdited);
        var toggled = hidden
            ? IconButton.Draw("eye", FontAwesomeIcon.EyeSlash, "Show", UiColours.Dim())
            : IconButton.RowAction("eye", FontAwesomeIcon.Eye, "Hide", rowHovered);
        if (toggled)
            Report(session.SetTracksHidden([track.Id], !hidden));
        ImGui.EndDisabled();
    }

    /// <summary>The width kept for the rightmost button in the header and every row, so the anchors above it line up.</summary>
    private static float LastSlot() =>
        MathF.Max(
            IconButton.Width(FontAwesomeIcon.Plus),
            MathF.Max(IconButton.Width(FontAwesomeIcon.Eye), IconButton.Width(FontAwesomeIcon.EyeSlash))
        );

    /// <summary>Continues the line so <paramref name="icon"/>'s button sits centred in the last slot.</summary>
    private static void CentreInLastSlot(FontAwesomeIcon icon) =>
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X + ((LastSlot() - IconButton.Width(icon)) * 0.5f));

    /// <summary>The name as a selectable spanning the row under its buttons, carrying the row's clicks, drag and drop, and context menu.</summary>
    private void DrawName(
        Scene scene,
        Track track,
        int index,
        IReadOnlyList<Guid> selected,
        bool editing,
        float nameWidth
    )
    {
        var picked = selected.Contains(track.Id);
        var group = RowPicking.IsGroup(selected, track.Id);
        if (
            ImGui.Selectable(
                "##name",
                picked,
                ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())
            )
        )
            Report(session.Selection.ClickTrack(track.Id, DragRows.Click()));
        RowText.Draw(track.Id, track.Name, nameWidth);
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            Report(game.FlyToFirstPoint(track.Id));

        if (editing)
            DragRows.Source(DragRows.Track, index, group, selected.Count, "tracks", track.Name);

        if (editing && ImGui.BeginDragDropTarget())
        {
            if (DragRows.Accept(DragRows.Track) is { } tracks)
            {
                var moving = DragRows.Tracks(session, scene, tracks);
                if (tracks.Grabbed < scene.Tracks.Count)
                    Report(session.MoveTracks(moving, scene.Tracks[tracks.Grabbed].Id, track.Id));
            }

            if (
                PointTransfer.CanTake(track, session.EditedTrackId)
                && DragRows.Accept(DragRows.Point, $"Add to {track.Name}") is { } points
            )
                Report(session.MovePointsTo(DragRows.Points(session, points), track.Id));
            ImGui.EndDragDropTarget();
        }

        if (!editing || !ImGui.BeginPopupContextItem("track-menu"))
            return;
        if (group)
            DrawGroupMenu(scene, selected);
        else
            DrawTrackMenu(scene, track);
        ImGui.EndPopup();
    }

    /// <summary>One track's menu: rename, duplicate, add to the playlist, save as a preset or delete it.</summary>
    private void DrawTrackMenu(Scene scene, Track track)
    {
        if (Menu.Item("Rename"))
            StartRename(track);
        if (Menu.Item("Duplicate"))
            Report(session.DuplicateTrack(track.Id));
        if (Menu.Item("Add to playlist"))
            Report(session.AddToPlaylist([track.Id]));
        if (Menu.Item("Save as preset", Presets.CanSave(track)))
        {
            presets = files.PresetNames();
            presetTrack = track.Id;
            AskName(Naming.SavePreset, track.Name);
        }

        if (Menu.Item("Delete", SceneEditing.CanDelete(scene, [track.Id])))
            Report(session.DeleteTracks([track.Id]));
    }

    /// <summary>The menu for several selected tracks: add them to the playlist, show, hide or delete them.</summary>
    private void DrawGroupMenu(Scene scene, IReadOnlyList<Guid> selected)
    {
        var edited = session.EditedTrackId;
        if (Menu.Item("Add to playlist"))
            Report(session.AddToPlaylist(selected));
        if (Menu.Item("Show", SceneEditing.CanShow(scene, selected)))
            Report(session.SetTracksHidden(selected, false));
        if (Menu.Item("Hide", SceneEditing.CanHide(scene, selected, edited)))
            Report(session.SetTracksHidden(selected, true));
        if (Menu.Item("Delete", SceneEditing.CanDelete(scene, selected)))
            Report(session.DeleteTracks(selected));
    }

    /// <summary>The space under the tracks: at least a row tall, it takes dropped tracks at the end and dropped points as a new track, and a click there clears the selection.</summary>
    private void DrawSpace(Scene scene, bool editing)
    {
        DragRows.Space(session, editing);
        if (!editing)
            return;

        if (DragRows.Dragging(DragRows.Point))
        {
            // Where a new track would go, in the row just under the last track.
            var at =
                ImGui.GetItemRectMin()
                + new Vector2(
                    ImGui.GetStyle().FramePadding.X * 2f,
                    (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f
                );
            var list = ImGui.GetWindowDrawList();
            var colour = UiColours.Muted();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                list.AddText(at, colour, FontAwesomeIcon.Plus.ToIconString());
                at.X += ImGui.CalcTextSize(FontAwesomeIcon.Plus.ToIconString()).X + ImGui.GetStyle().ItemInnerSpacing.X;
            }

            list.AddText(at, colour, "New track");
        }

        if (!ImGui.BeginDragDropTarget())
            return;
        if (DragRows.Accept(DragRows.Track) is { } tracks && tracks.Grabbed < scene.Tracks.Count)
            Report(session.MoveTracks(DragRows.Tracks(session, scene, tracks), scene.Tracks[tracks.Grabbed].Id, null));
        if (DragRows.Accept(DragRows.Point, "New track") is { } points)
            Report(session.MovePointsTo(DragRows.Points(session, points), null));
        ImGui.EndDragDropTarget();
    }

    /// <summary>The name as a text field; Enter or clicking away renames, Escape cancels.</summary>
    private void DrawRename(Track track, float width)
    {
        var result = TextEdit.Draw("##rename", ref renameText, NameBuffer, width, focusRename);
        focusRename = false;
        if (result == TextEdit.Result.Editing)
            return;
        if (result == TextEdit.Result.Apply)
            Report(session.RenameTrack(track.Id, renameText));
        renaming = null;
    }

    private void StartRename(Track track)
    {
        renaming = track.Id;
        renameText = track.Name;
        focusRename = true;
    }
}
