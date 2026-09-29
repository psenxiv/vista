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
using Vista.Plugin.Ui.Windows;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The open scene's tracks: pick one to edit, show or hide, rename, duplicate, delete, reorder, and save or add presets.</summary>
internal sealed class HierarchyPanel
{
    private readonly GameSession game;
    private readonly SessionState session;
    private readonly SceneFiles files;
    private readonly FilePickerWindow picker;
    private readonly PresetSave presetSave;
    private Guid? renaming;
    private string renameText = string.Empty;
    private bool focusRename;

    public HierarchyPanel(GameSession game, SceneFiles files, FilePickerWindow picker, PresetSave presetSave)
    {
        this.game = game;
        session = game.State;
        this.files = files;
        this.picker = picker;
        this.presetSave = presetSave;
    }

    /// <summary>The Scene heading, the scene anchor button and the add button, the open scene's name, then one row per track; the buttons and rows are disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        var headerWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - ButtonsWidth());
        var headerStart = ImGui.GetCursorPosX();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted("Scene");
        ImGui.SameLine();
        ImGui.SetCursorPosX(headerStart + headerWidth + ImGui.GetStyle().ItemSpacing.X);

        ImGui.BeginDisabled(!editing);
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor"))
            Report(session.Selection.SelectSceneAnchor());
        ImGui.EndDisabled();

        CentreInLastSlot(FontAwesomeIcon.Plus);
        if (IconButton.Draw("add-track", FontAwesomeIcon.Plus, "Add track or preset"))
            ImGui.OpenPopup("add-track-menu");
        DrawAddMenu();
        ImGui.EndDisabled();
        NameStrip.Draw("scene-name", files.CurrentName);
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
    }

    /// <summary>Add track's menu: an empty track, or the preset picker.</summary>
    private void DrawAddMenu()
    {
        if (!ImGui.BeginPopup("add-track-menu"))
            return;

        if (Menu.Item("Add track"))
            Report(session.AddTrack());
        if (Menu.Item("Add preset"))
            picker.Show(FilePickerKind.Preset);
        ImGui.EndPopup();
    }

    /// <summary>The name, then the anchor button and the eye, shown on hover (a hidden track's eye always): click edits the track, double-click flies to its first point, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, IReadOnlyList<Guid> selected, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - ButtonsWidth());
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

    /// <summary>The width kept for the rightmost button in the header and every row, so they line up.</summary>
    private static float LastSlot() =>
        MathF.Max(
            IconButton.Width(FontAwesomeIcon.Plus),
            MathF.Max(IconButton.Width(FontAwesomeIcon.Eye), IconButton.Width(FontAwesomeIcon.EyeSlash))
        );

    /// <summary>The name's gap before the buttons, then the anchor button and the last slot, reserved from both the header and every row so their names line up.</summary>
    private static float ButtonsWidth() =>
        ImGui.GetStyle().ItemSpacing.X + IconButton.RowWidth(IconButton.Width(FontAwesomeIcon.Anchor), LastSlot());

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
            presetSave.Ask(track);

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
        var result = TextEdit.Draw("##rename", ref renameText, NamePrompt.NameBuffer, width, focusRename);
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
