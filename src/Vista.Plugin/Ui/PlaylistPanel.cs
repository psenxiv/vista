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

    private const float LoopWidth = 44f;

    private readonly CameraSession session;

    private Guid? editingLoops;
    private string loopsText = string.Empty;
    private bool focusLoops;

    // Wheel travel over the loop cells not yet taken as a step, so a trackpad steps once per notch.
    private float wheelCarry;
    private bool loopsHovered;

    public PlaylistPanel(CameraSession session) => this.session = session;

    /// <summary>The header with its loop and add buttons, then one row per entry; editing is disabled unless in Edit mode.</summary>
    public void Draw(bool editing)
    {
        // Rows can remove or reorder entries, so every row reads this snapshot.
        var scene = session.Scene;
        var playing = session.PlayingEntry;
        if (editingLoops is { } id && (!editing || PlaylistEditing.IndexOf(scene, id) < 0)) editingLoops = null;
        loopsHovered = false;

        ImGui.AlignTextToFramePadding();
        var header = playing is { } now
            ? $"{PlaylistEditing.IndexOf(scene, now.Id) + 1} / {scene.Playlist.Count} — {SceneEditing.Get(scene, now.TrackId).Name}"
            : "Playlist";
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted(), playing is null))
            ImGui.TextUnformatted(header);

        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.Width(FontAwesomeIcon.Repeat) + IconButton.Width(FontAwesomeIcon.Plus) + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons));
        if (IconButton.Toggle("playlist-loop", FontAwesomeIcon.Repeat, scene.PlaylistLoops, "Loop playlist"))
            Report(session.SetPlaylistLoops(!scene.PlaylistLoops));
        ImGui.SameLine();
        if (IconButton.Draw("add-entry", FontAwesomeIcon.Plus, "Add to playlist")) ImGui.OpenPopup("add-entry");
        if (ImGui.BeginPopup("add-entry"))
        {
            foreach (var track in scene.Tracks)
            {
                using var trackId = ImRaii.PushId(track.Id.ToString());
                if (ImGui.Selectable(track.Name)) Report(session.AddToPlaylist(track.Id));
            }

            ImGui.EndPopup();
        }

        ImGui.EndDisabled();
        ImGui.Separator();

        ImGui.BeginDisabled(!editing);
        if (ImGui.BeginChild("entries", new Vector2(0f, 0f)))
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
        ImGui.EndDisabled();
        if (!loopsHovered) wheelCarry = 0f;
    }

    /// <summary>One entry: its number and track, drag to reorder or drop a track on it, its loop cell and its remove button, shown on hover; greyed when never reached.</summary>
    private void DrawRow(Scene scene, PlaylistEntry entry, int index, bool unreachable, Guid? playing, bool editing)
    {
        using var id = ImRaii.PushId(entry.Id.ToString());
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f, unreachable);

        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f));
        var name = SceneEditing.Get(scene, entry.TrackId).Name;
        ImGui.Selectable($"{index + 1}  {name}", entry.Id == playing, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(nameWidth, ImGui.GetFrameHeight()));
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, ImGui.GetItemRectMax().Y);
        var rowHovered = editing && IconButton.RowHovered(rowMin, rowMax);

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(EntryPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(name);
            ImGui.EndDragDropSource();
        }

        DropTarget(scene, index, editing);

        ImGui.SameLine();
        DrawLoops(scene, entry, editing);

        ImGui.SameLine();
        if (IconButton.RowAction("remove", FontAwesomeIcon.Times, "Remove from playlist", rowHovered, danger: true)) Report(session.RemoveFromPlaylist(entry.Id));
    }

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
        if (editing) ImGuiP.SetItemUsingMouseWheel();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Repeat Count");
        if (!editing || !ImGui.IsItemHovered()) return;
        loopsHovered = true;
        StepLoops(entry);
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
        var digits = new string(loopsText.Where(char.IsAsciiDigit).ToArray());
        var count = int.TryParse(digits, out var n) && n > 0 ? n : (int?)null;
        Report(session.SetEntryLoops(entry.Id, count));
        editingLoops = null;
    }

    private void StartLoops(PlaylistEntry entry)
    {
        editingLoops = entry.Id;
        loopsText = entry.Loops?.ToString() ?? string.Empty;
        focusLoops = true;
    }

    /// <summary>Each whole notch of the mouse wheel steps the count by one: down from 1 empties it, up from empty gives 1.</summary>
    private void StepLoops(PlaylistEntry entry)
    {
        wheelCarry += ImGui.GetIO().MouseWheel;
        var loops = entry.Loops;
        while (MathF.Abs(wheelCarry) >= 1f)
        {
            var up = wheelCarry > 0f;
            wheelCarry -= up ? 1f : -1f;
            int? next = up
                ? Math.Min((loops ?? 0) + 1, PlaylistEditing.MaxLoops)
                : loops is { } n && n > 1 ? n - 1 : null;
            if (next == loops) continue;
            Report(session.SetEntryLoops(entry.Id, next));
            loops = next;
        }
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
