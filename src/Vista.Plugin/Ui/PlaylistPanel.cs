using System.Numerics;
using Vista.Core.Scenes;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>The playlist Live plays: add, reorder, remove, set loop counts, and see what's playing.</summary>
internal sealed unsafe class PlaylistPanel
{
    /// <summary>The compartment's width.</summary>
    public const float Width = 240f;

    private const string EntryPayload = "VISTA_ENTRY";

    // The Hierarchy's row payload: the dragged track's index in the scene.
    private const string TrackPayload = "VISTA_TRACK";

    private const uint Amber = 0xFF40C0FF;
    private const float LoopWidth = 44f;

    private readonly CameraSession session;

    // A loop count being dragged or typed, held across frames until let go.
    private (Guid Entry, int Value)? pendingLoops;

    public PlaylistPanel(CameraSession session) => this.session = session;

    /// <summary>The header, one row per entry, and + Add; editing is disabled unless in Edit mode.</summary>
    public void Draw(bool editing)
    {
        // Rows can remove or reorder entries, so every row reads this snapshot.
        var scene = session.Scene;
        var playing = session.PlayingEntry;

        ImGui.AlignTextToFramePadding();
        if (playing is { } now)
            ImGui.TextUnformatted($"{PlaylistEditing.IndexOf(scene, now.Id) + 1} / {scene.Playlist.Count} — {SceneEditing.Get(scene, now.TrackId).Name}");
        else
            ImGui.TextUnformatted("Playlist");
        ImGui.Separator();

        ImGui.BeginDisabled(!editing);
        var footer = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("entries", new Vector2(0f, -footer)))
        {
            var held = false;
            for (var i = 0; i < scene.Playlist.Count; i++)
            {
                DrawRow(scene, scene.Playlist[i], i, held, playing?.Id, editing);
                held |= PlaylistEditing.HoldsPlaylist(scene, scene.Playlist[i]);
            }

            // The space under the rows takes a dropped track at the end.
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight())));
            DropTarget(scene, scene.Playlist.Count, editing);
        }

        ImGui.EndChild();

        if (ImGui.Button("+ Add")) ImGui.OpenPopup("add-entry");
        if (ImGui.BeginPopup("add-entry"))
        {
            foreach (var track in scene.Tracks)
            {
                using var id = ImRaii.PushId(track.Id.ToString());
                if (ImGui.Selectable(track.Name)) Report(session.AddToPlaylist(track.Id));
            }

            ImGui.EndPopup();
        }

        ImGui.EndDisabled();
    }

    /// <summary>One entry: its number and track, drag to reorder or drop a track on it, its loop cell and its remove button; greyed when never reached.</summary>
    private void DrawRow(Scene scene, PlaylistEntry entry, int index, bool unreachable, Guid? playing, bool editing)
    {
        using var id = ImRaii.PushId(entry.Id.ToString());
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f, unreachable);

        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f));
        var name = SceneEditing.Get(scene, entry.TrackId).Name;
        ImGui.Selectable($"{index + 1}  {name}", entry.Id == playing, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(nameWidth, ImGui.GetFrameHeight()));

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(EntryPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(name);
            ImGui.EndDragDropSource();
        }

        DropTarget(scene, index, editing);

        ImGui.SameLine();
        DrawLoops(scene, entry);

        ImGui.SameLine();
        if (IconButton.Draw("remove", FontAwesomeIcon.Times, "Remove from playlist")) Report(session.RemoveFromPlaylist(entry.Id));
    }

    /// <summary>The loop count: 0 follows the track (— or ∞ when it holds the playlist), otherwise ×N; set when let go.</summary>
    private void DrawLoops(Scene scene, PlaylistEntry entry)
    {
        var value = pendingLoops is { } p && p.Entry == entry.Id ? p.Value : entry.Loops ?? 0;
        var holds = PlaylistEditing.HoldsPlaylist(scene, entry);
        var format = value > 0 ? "×%d" : holds ? "∞" : "—";
        ImGui.SetNextItemWidth(LoopWidth);
        using (ImRaii.PushColor(ImGuiCol.Text, Amber, value > 0 || holds))
            ImGui.DragInt("##loops", ref value, 0.05f, 0, PlaylistEditing.MaxLoops, format);
        if (ImGui.IsItemActive()) pendingLoops = (entry.Id, value);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Times to play; — follows the track (∞ when the track loops)");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Report(session.SetEntryLoops(entry.Id, value <= 0 ? null : value));
            pendingLoops = null;
        }
        else if (ImGui.IsItemDeactivated() && pendingLoops is { } q && q.Entry == entry.Id)
            pendingLoops = null;
    }

    /// <summary>Accepts an entry (to reorder) or a Hierarchy track (to add) dropped on the last item, placing it at <paramref name="index"/>.</summary>
    private void DropTarget(Scene scene, int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;

        var entry = ImGui.AcceptDragDropPayload(EntryPayload);
        if (!entry.IsNull && *(int*)entry.Handle->Data is var from && from != index)
            Report(session.MovePlaylistEntry(from, Math.Min(index, scene.Playlist.Count - 1)));

        var track = ImGui.AcceptDragDropPayload(TrackPayload);
        if (!track.IsNull && *(int*)track.Handle->Data is var t && t >= 0 && t < scene.Tracks.Count)
            Report(session.AddToPlaylist(scene.Tracks[t].Id, index));

        ImGui.EndDragDropTarget();
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
