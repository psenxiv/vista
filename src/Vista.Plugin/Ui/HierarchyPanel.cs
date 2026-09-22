using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>The scene's tracks: pick one to edit, show or hide, rename, duplicate, delete and reorder them.</summary>
internal sealed unsafe class HierarchyPanel
{
    /// <summary>The compartment's width.</summary>
    public const float Width = 200f;

    private const string TrackPayload = "VISTA_TRACK";

    private readonly CameraSession session;
    private Guid? renaming;
    private string renameText = string.Empty;
    private bool focusRename;

    public HierarchyPanel(CameraSession session) => this.session = session;

    /// <summary>The "Scene" header, one row per track, and + Track; disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Scene");
        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + IconButton.Width(FontAwesomeIcon.StreetView) + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons));
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor")) Report(session.SelectSceneAnchor());
        ImGui.SameLine();
        if (IconButton.Draw("bring-scene", FontAwesomeIcon.StreetView, "Bring scene to me")) Report(session.BringSceneToMe());
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.Separator();

        // Rows can delete or reorder tracks, so every row reads this snapshot.
        var scene = session.Scene;
        var edited = session.EditedTrackId;
        if (renaming is { } id && (!editing || SceneEditing.IndexOf(scene, id) < 0)) renaming = null;
        ImGui.BeginDisabled(!editing);
        var footer = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("tracks", new Vector2(0f, -footer)))
        {
            for (var i = 0; i < scene.Tracks.Count; i++) DrawRow(scene, scene.Tracks[i], i, edited, editing);
        }

        ImGui.EndChild();
        if (ImGui.Button("+ Track")) Report(session.AddTrack());
        ImGui.EndDisabled();
    }

    /// <summary>The eye toggle and the name: click edits the track, double-click renames, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);

        ImGui.BeginDisabled(isEdited);
        if (IconButton.Draw("eye", hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, hidden ? "Show" : "Hide"))
            Report(session.SetTrackHidden(track.Id, !hidden));
        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginDisabled(!track.AnchorPlaced);
        if (IconButton.Draw("anchor", FontAwesomeIcon.Anchor, "Select track anchor")) Report(session.SelectTrackAnchor(track.Id));
        ImGui.EndDisabled();
        ImGui.SameLine();

        if (renaming == track.Id)
        {
            DrawRename(track);
            return;
        }

        if (ImGui.Selectable(track.Name, isEdited, ImGuiSelectableFlags.None, new Vector2(0f, ImGui.GetFrameHeight())))
            Report(session.OpenTrack(track.Id));
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartRename(track);

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(TrackPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(track.Name);
            ImGui.EndDragDropSource();
        }

        if (editing && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(TrackPayload);
            if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index) Report(session.MoveTrack(from, index));
            ImGui.EndDragDropTarget();
        }

        if (editing && ImGui.BeginPopupContextItem("track-menu"))
        {
            var ticked = false;
            if (ImGui.MenuItem("Rename", string.Empty, ref ticked)) StartRename(track);
            if (ImGui.MenuItem("Duplicate", string.Empty, ref ticked)) Report(session.DuplicateTrack(track.Id));
            if (ImGui.MenuItem("Add to playlist", string.Empty, ref ticked)) Report(session.AddToPlaylist(track.Id));
            if (ImGui.MenuItem("Delete", string.Empty, ref ticked, scene.Tracks.Count > 1)) Report(session.DeleteTrack(track.Id));
            ImGui.EndPopup();
        }
    }

    /// <summary>The name as a text field; Enter or clicking away renames, Escape cancels.</summary>
    private void DrawRename(Track track)
    {
        if (focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            focusRename = false;
        }

        ImGui.SetNextItemWidth(-1f);
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

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
