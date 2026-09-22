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

    /// <summary>The "Scene" header with its anchor and add buttons, then one row per track; disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted("Scene");
        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + LastSlot() + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons));
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor")) Report(session.SelectSceneAnchor());
        ImGui.EndDisabled();
        CentreInLastSlot(FontAwesomeIcon.Plus);
        if (IconButton.Draw("add-track", FontAwesomeIcon.Plus, "Add track")) Report(session.AddTrack());
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
    }

    /// <summary>The name, then the anchor button and the eye: click edits the track, double-click flies to its first point, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + LastSlot() + (ImGui.GetStyle().ItemSpacing.X * 2f);
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons);

        if (renaming == track.Id) DrawRename(track, nameWidth);
        else DrawName(scene, track, index, isEdited, editing, nameWidth);
        ImGui.SameLine();

        var follows = track.Aim == AimMode.FollowTarget;
        ImGui.BeginDisabled(!track.AnchorPlaced || follows);
        var anchorTip = follows ? "Follow Target tracks move with their character" : "Select track anchor";
        if (IconButton.Draw("anchor", FontAwesomeIcon.Anchor, anchorTip)) Report(session.SelectTrackAnchor(track.Id));
        ImGui.EndDisabled();
        var eye = hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye;
        CentreInLastSlot(eye);

        ImGui.BeginDisabled(isEdited);
        if (IconButton.Draw("eye", hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, hidden ? "Show" : "Hide", hidden ? UiColours.Dim() : null))
            Report(session.SetTrackHidden(track.Id, !hidden));
        ImGui.EndDisabled();
    }

    /// <summary>The width kept for the rightmost button in the header and every row, so the anchors above it line up.</summary>
    private static float LastSlot()
        => MathF.Max(IconButton.Width(FontAwesomeIcon.Plus), MathF.Max(IconButton.Width(FontAwesomeIcon.Eye), IconButton.Width(FontAwesomeIcon.EyeSlash)));

    /// <summary>Continues the line so <paramref name="icon"/>'s button sits centred in the last slot.</summary>
    private static void CentreInLastSlot(FontAwesomeIcon icon)
        => ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X + ((LastSlot() - IconButton.Width(icon)) * 0.5f));

    /// <summary>The name as a selectable, carrying the row's clicks, drag and drop, and context menu.</summary>
    private void DrawName(Scene scene, Track track, int index, bool isEdited, bool editing, float width)
    {
        if (ImGui.Selectable("##name", isEdited, ImGuiSelectableFlags.None, new Vector2(width, ImGui.GetFrameHeight())))
            Report(session.SelectTrack(track.Id));
        RowText.Draw(track.Name);
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

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
