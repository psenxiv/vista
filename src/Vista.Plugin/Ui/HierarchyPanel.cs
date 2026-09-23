using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using static Vista.Plugin.Ui.Refusal;
namespace Vista.Plugin.Ui;

/// <summary>The scene selector and the scene's tracks: pick one to edit, show or hide, rename, duplicate, delete, reorder, and save or add presets.</summary>
internal sealed unsafe class HierarchyPanel
{
    /// <summary>The compartment's width.</summary>
    public const float Width = 200f;

    private const string TrackPayload = "VISTA_TRACK";

    private const string NamePopup = "Name###vista-name";
    private const string DeletePopup = "Delete###vista-delete";

    /// <summary>What the name prompt is naming.</summary>
    private enum Naming { NewScene, RenameScene, DuplicateScene, SavePreset }

    private readonly CameraSession session;
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

    public HierarchyPanel(CameraSession session, SceneFiles files)
    {
        this.session = session;
        this.files = files;
    }

    /// <summary>The scene selector with its anchor and add buttons, then one row per track; disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + LastSlot() + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons - ImGui.GetStyle().ItemSpacing.X));
        DrawSelector();
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons));
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor")) Report(session.SelectSceneAnchor());
        ImGui.EndDisabled();
        CentreInLastSlot(FontAwesomeIcon.Plus);
        if (IconButton.Draw("add-track", FontAwesomeIcon.Plus, "Add track")) ImGui.OpenPopup("add-track-menu");
        DrawAddMenu();
        ImGui.EndDisabled();
        ImGui.Separator();

        // Rows can delete or reorder tracks, so every row reads this snapshot.
        var scene = session.Scene;
        var edited = session.EditedTrackId;
        if (renaming is { } id && (!editing || SceneEditing.IndexOf(scene, id) < 0)) renaming = null;
        ImGui.BeginDisabled(!editing);
        if (ImGui.BeginChild("tracks", new Vector2(0f, 0f)))
        {
            for (var i = 0; i < scene.Tracks.Count; i++) DrawRow(scene, scene.Tracks[i], i, edited, editing);
        }

        ImGui.EndChild();
        ImGui.EndDisabled();

        // Opened here, outside the menus that asked for them, so the popups share one ID scope.
        if (openName) { ImGui.OpenPopup(NamePopup); openName = false; }
        if (openDelete) { ImGui.OpenPopup(DeletePopup); openDelete = false; }
        DrawNamePrompt();
        DrawDeleteConfirm();
    }

    /// <summary>The open scene's name as a drop-down: every scene to switch to, then New, Rename, Duplicate and Delete; the list is read when it opens.</summary>
    private void DrawSelector()
    {
        var current = files.CurrentName;
        if (!ImGui.BeginCombo("##scene", current.Length > 0 ? current : "Scene")) return;
        if (ImGui.IsWindowAppearing()) scenes = files.Scenes();

        foreach (var name in scenes)
        {
            using var id = ImRaii.PushId(name);
            if (ImGui.Selectable(name, name == current) && name != current) Report(files.Switch(name));
        }

        ImGui.Separator();
        if (ImGui.Selectable("New scene")) AskName(Naming.NewScene, SceneNames.NextFree("Scene", scenes));
        if (ImGui.Selectable("Rename scene")) AskName(Naming.RenameScene, current);
        if (ImGui.Selectable("Duplicate scene")) AskName(Naming.DuplicateScene, SceneNames.CopyOf(current, scenes));
        if (ImGui.Selectable("Delete scene")) { deleting = (false, current); openDelete = true; }
        ImGui.Separator();
        if (ImGui.Selectable("Open folder")) files.OpenFolder(presets: false);
        ImGui.EndCombo();
    }

    /// <summary>Add track's menu: an empty track, or a preset; right-click a preset to delete it. The presets are read when it opens.</summary>
    private void DrawAddMenu()
    {
        if (!ImGui.BeginPopup("add-track-menu")) return;
        if (ImGui.IsWindowAppearing()) presets = files.Presets();

        var ticked = false;
        if (ImGui.MenuItem("Empty track", string.Empty, ref ticked)) Report(session.AddTrack());
        if (ImGui.BeginMenu("From preset"))
        {
            if (presets.Count == 0)
            {
                ImGui.BeginDisabled();
                ImGui.MenuItem("No presets", string.Empty, ref ticked);
                ImGui.EndDisabled();
            }

            foreach (var name in presets)
            {
                using var id = ImRaii.PushId(name);
                if (ImGui.MenuItem(name, string.Empty, ref ticked)) Report(files.AddPreset(name));
                if (ImGui.BeginPopupContextItem("preset-menu"))
                {
                    if (ImGui.MenuItem("Delete", string.Empty, ref ticked)) { deleting = (true, name); openDelete = true; }
                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Open folder", string.Empty, ref ticked)) files.OpenFolder(presets: true);
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
        if (!ImGui.BeginPopupModal(NamePopup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (naming is not { } what) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

        ImGui.TextUnformatted(what switch
        {
            Naming.NewScene => "New scene",
            Naming.RenameScene => "Rename scene",
            Naming.DuplicateScene => "Duplicate scene",
            _ => "Save as preset",
        });
        if (focusName) { ImGui.SetKeyboardFocusHere(); focusName = false; }
        ImGui.SetNextItemWidth(260f);
        var entered = ImGui.InputText("##name", ref nameText, SceneNames.MaxLength + 8, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        // Checked when the text changes, not every frame, since a scene check lists the folder.
        if (checkedName is not { } check || check.Text != nameText)
        {
            var preset = what == Naming.SavePreset;
            var nameRefusal = preset ? SceneNames.Refusal(nameText) : files.NameRefusal(nameText, what == Naming.RenameScene);
            checkedName = check = (nameText, nameRefusal, preset && nameRefusal is null && SceneNames.Taken(nameText, presets));
        }

        var (_, refusal, replaces) = check;
        // Always a line here, blank when the name is fine, so the buttons don't jump as you type.
        using (ImRaii.PushColor(ImGuiCol.Text, refusal is not null ? UiColours.Red : UiColours.Muted()))
            ImGui.TextUnformatted(refusal ?? (replaces ? $"A preset called {nameText.Trim()} exists" : " "));

        ImGui.BeginDisabled(refusal is not null);
        var ok = ImGui.Button(replaces ? "Replace" : "Ok", new Vector2(127f, 0f)) || (entered && refusal is null);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(127f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) { naming = null; ImGui.CloseCurrentPopup(); }

        if (ok && refusal is null)
        {
            var name = nameText.Trim();
            Report(what switch
            {
                Naming.NewScene => files.New(name),
                Naming.RenameScene => files.Rename(name),
                Naming.DuplicateScene => files.Duplicate(name),
                _ => files.SavePreset(name, presetTrack),
            });
            naming = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>"Delete name? This can't be undone." for a scene or a preset.</summary>
    private void DrawDeleteConfirm()
    {
        if (!ImGui.BeginPopupModal(DeletePopup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (deleting is not { } target) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

        ImGui.TextUnformatted($"Delete {target.Name}? This can't be undone.");
        if (ImGui.Button("Delete", new Vector2(127f, 0f)))
        {
            Report(target.Preset ? files.DeletePreset(target.Name) : files.Delete());
            deleting = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(127f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) { deleting = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    /// <summary>The name, then the anchor button and the eye, shown on hover (a hidden track's eye always): click edits the track, double-click flies to its first point, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + LastSlot() + (ImGui.GetStyle().ItemSpacing.X * 2f);
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons);
        var rowMin = ImGui.GetCursorScreenPos();
        var rowMax = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, rowMin.Y + ImGui.GetFrameHeight());
        var rowHovered = editing && IconButton.RowHovered(rowMin, rowMax);

        var rowStart = ImGui.GetCursorPosX();
        if (renaming == track.Id) DrawRename(track, nameWidth);
        else DrawName(scene, track, index, isEdited, editing, nameWidth);
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + nameWidth + ImGui.GetStyle().ItemSpacing.X);

        var follows = track.Aim == AimMode.FollowTarget;
        ImGui.BeginDisabled(!track.AnchorPlaced || follows);
        var anchorTip = follows ? "Follow Target tracks move with their character" : "Select track anchor";
        if (IconButton.RowAction("anchor", FontAwesomeIcon.Anchor, anchorTip, rowHovered)) Report(session.SelectTrackAnchor(track.Id));
        ImGui.EndDisabled();
        var eye = hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye;
        CentreInLastSlot(eye);

        // A hidden track keeps its eye showing, so you can see what's hidden without hovering.
        ImGui.BeginDisabled(isEdited);
        var toggled = hidden
            ? IconButton.Draw("eye", FontAwesomeIcon.EyeSlash, "Show", UiColours.Dim())
            : IconButton.RowAction("eye", FontAwesomeIcon.Eye, "Hide", rowHovered);
        if (toggled) Report(session.SetTrackHidden(track.Id, !hidden));
        ImGui.EndDisabled();
    }

    /// <summary>The width kept for the rightmost button in the header and every row, so the anchors above it line up.</summary>
    private static float LastSlot()
        => MathF.Max(IconButton.Width(FontAwesomeIcon.Plus), MathF.Max(IconButton.Width(FontAwesomeIcon.Eye), IconButton.Width(FontAwesomeIcon.EyeSlash)));

    /// <summary>Continues the line so <paramref name="icon"/>'s button sits centred in the last slot.</summary>
    private static void CentreInLastSlot(FontAwesomeIcon icon)
        => ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X + ((LastSlot() - IconButton.Width(icon)) * 0.5f));

    /// <summary>The name as a selectable spanning the row under its buttons, carrying the row's clicks, drag and drop, and context menu.</summary>
    private void DrawName(Scene scene, Track track, int index, bool isEdited, bool editing, float nameWidth)
    {
        if (ImGui.Selectable("##name", isEdited, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())))
            Report(session.SwitchTrack(track.Id));
        RowText.Draw(track.Name, nameWidth);
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Report(session.FlyToFirstPoint(track.Id));

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(TrackPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(track.Name);
            ImGui.EndDragDropSource();
        }

        if (editing && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(TrackPayload);
            if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index && from < scene.Tracks.Count)
                Report(session.MoveTracks([scene.Tracks[from].Id], scene.Tracks[from].Id, track.Id));
            ImGui.EndDragDropTarget();
        }

        if (editing && ImGui.BeginPopupContextItem("track-menu"))
        {
            var ticked = false;
            if (ImGui.MenuItem("Rename", string.Empty, ref ticked)) StartRename(track);
            if (ImGui.MenuItem("Duplicate", string.Empty, ref ticked)) Report(session.DuplicateTrack(track.Id));
            if (ImGui.MenuItem("Add to playlist", string.Empty, ref ticked)) Report(session.AddToPlaylist(track.Id));
            if (ImGui.MenuItem("Save as preset", string.Empty, ref ticked, track.Points.Count > 0))
            {
                presets = files.Presets();
                presetTrack = track.Id;
                AskName(Naming.SavePreset, track.Name);
            }

            if (ImGui.MenuItem("Delete", string.Empty, ref ticked, scene.Tracks.Count > 1)) Report(session.DeleteTrack(track.Id));
            ImGui.EndPopup();
        }
    }

    /// <summary>The name as a text field; Enter or clicking away renames, Escape cancels.</summary>
    private void DrawRename(Track track, float width)
    {
        if (focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            focusRename = false;
        }

        ImGui.SetNextItemWidth(width);
        var entered = ImGui.InputText("##rename", ref renameText, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            renaming = null;
            return;
        }

        if (!entered && !ImGui.IsItemDeactivated()) return;
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
