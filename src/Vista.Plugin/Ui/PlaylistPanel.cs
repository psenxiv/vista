using System.Globalization;
using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using static Vista.Plugin.Ui.Refusal;
namespace Vista.Plugin.Ui;

/// <summary>The playlist Live plays: add, reorder, remove, set loop counts, and see what's playing.</summary>
internal sealed class PlaylistPanel
{
    private const float LoopWidth = 44f;

    private readonly CameraSession session;

    // A repeat count being dragged, applied when the field is let go.
    private (Guid Id, int Value)? loopsDrag;

    // A repeat count being typed, and whether its field still needs focus.
    private (Guid Id, string Text, bool Focus)? loopsTyping;

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
        if (loopsDrag is { } drag && (!editing || PlaylistEditing.IndexOf(scene, drag.Id) < 0)) loopsDrag = null;
        if (loopsTyping is { } typed && (!editing || PlaylistEditing.IndexOf(scene, typed.Id) < 0)) loopsTyping = null;
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
                if (ImGui.Selectable(track.Name)) Report(session.AddToPlaylist([track.Id]));
            }

            ImGui.EndPopup();
        }

        ImGui.EndDisabled();
        ImGui.Separator();

        ImGui.BeginDisabled(!editing);
        if (ImGui.BeginChild("entries", new Vector2(0f, 0f)))
        {
            var held = false;
            var selected = session.SelectedEntries;
            for (var i = 0; i < scene.Playlist.Count; i++)
            {
                DrawRow(scene, scene.Playlist[i], i, held, playing?.Id, selected, editing);
                held |= PlaylistEditing.HoldsPlaylist(scene, scene.Playlist[i]);
            }

            // The space under the rows takes dropped rows at the end, and a click there clears the selection.
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight())));
            if (editing && ImGui.IsItemClicked() && DragRows.Click() == RowClick.Plain) session.Select(null);
            DropTarget(scene, scene.Playlist.Count, editing);
        }

        ImGui.EndChild();
        ImGui.EndDisabled();
        if (!loopsHovered) wheelCarry = 0f;
    }

    /// <summary>One entry: its number and track, a warning when its watched or followed character isn't found, click to select, drag to reorder or drop tracks on it, right-click several for their menu, its loop cell and its remove button, shown on hover; greyed when never reached.</summary>
    private void DrawRow(Scene scene, PlaylistEntry entry, int index, bool unreachable, Guid? playing, IReadOnlyList<Guid> selected, bool editing)
    {
        using var id = ImRaii.PushId(entry.Id.ToString());
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f, unreachable);

        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var track = SceneEditing.Get(scene, entry.TrackId);
        var lost = session.TargetLost(session.WorldOf(track));
        var warning = lost ? IconButton.WarningWidth() + gap : 0f;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f) - warning);
        var name = track.Name;
        var rowStart = ImGui.GetCursorPosX();
        var picked = selected.Contains(entry.Id);
        var group = picked && selected.Count >= 2;
        if (ImGui.Selectable("##entry", editing ? picked : entry.Id == playing, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())))
            Report(session.ClickEntry(entry.Id, DragRows.Click()));
        RowText.Draw(entry.Id, $"{index + 1}  {name}", nameWidth);
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, ImGui.GetItemRectMax().Y);
        var rowHovered = editing && IconButton.RowHovered(rowMin, rowMax);

        if (editing && ImGui.BeginDragDropSource())
        {
            DragRows.Carry(DragRows.Entry, index, group, group ? $"{selected.Count} rows" : name);
            ImGui.EndDragDropSource();
        }

        DropTarget(scene, index, editing);
        if (editing && group && ImGui.BeginPopupContextItem("entry-menu"))
        {
            var ticked = false;
            if (ImGui.MenuItem("Remove from playlist", string.Empty, ref ticked)) Report(session.RemoveFromPlaylist(selected));
            ImGui.EndPopup();
        }

        // The selectable spans the row, so the row's other items go back over it.
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + nameWidth + gap);
        if (lost)
        {
            IconButton.TargetNotFound(track.Aim == AimMode.FollowTarget ? IconButton.FollowNotFoundTooltip : IconButton.NotFoundTooltip);
            ImGui.SameLine();
        }

        DrawLoops(scene, entry, editing);

        ImGui.SameLine();
        if (IconButton.RowAction("remove", FontAwesomeIcon.Times, "Remove from playlist", rowHovered, danger: true)) Report(session.RemoveFromPlaylist([entry.Id]));
    }

    /// <summary>The repeat count as a drag field: 1 up, or 0 to follow the track, shown as ∞ when that holds the playlist or — when it plays once. Double-click or Ctrl + click to type, wheel to step; a drag applies when let go.</summary>
    private void DrawLoops(Scene scene, PlaylistEntry entry, bool editing)
    {
        if (loopsTyping is { } typing && typing.Id == entry.Id)
        {
            DrawLoopsText(entry, typing);
            return;
        }

        var holds = PlaylistEditing.HoldsPlaylist(scene, entry);
        var value = loopsDrag is { } drag && drag.Id == entry.Id ? drag.Value : entry.Loops ?? 0;
        var format = value > 0 ? "%d" : holds ? "∞" : "—";
        var colour = value > 0 || holds ? UiColours.Amber : UiColours.Dim();

        ImGui.SetNextItemWidth(LoopWidth);
        bool changed;
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
            changed = ImGui.DragInt("##loops", ref value, 0.1f, 0, PlaylistEditing.MaxLoops, format, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput);
        if (changed) loopsDrag = (entry.Id, value);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Repeats");

        // ImGui's own typing reads the text through the display format, which has no number when it shows — or ∞.
        if (editing && ImGui.IsItemHovered() && (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) || (ImGui.IsItemClicked() && ImGui.GetIO().KeyCtrl)))
        {
            loopsDrag = null;
            loopsTyping = (entry.Id, entry.Loops?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, true);
            return;
        }
        if (loopsDrag is { } done && done.Id == entry.Id && !ImGui.IsItemActive())
        {
            loopsDrag = null;
            Report(session.SetEntryLoops(entry.Id, done.Value > 0 ? done.Value : null));
        }

        if (editing) ImGuiP.SetItemUsingMouseWheel();
        if (!editing || !ImGui.IsItemHovered()) return;
        loopsHovered = true;
        StepLoops(entry);
    }

    /// <summary>The repeat count as text: Enter or clicking away applies it, blank or 0 follows the track, Escape cancels.</summary>
    private void DrawLoopsText(PlaylistEntry entry, (Guid Id, string Text, bool Focus) typing)
    {
        if (typing.Focus) ImGui.SetKeyboardFocusHere();
        var text = typing.Text;
        ImGui.SetNextItemWidth(LoopWidth);
        var entered = ImGui.InputText("##loops-text", ref text, 8, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsDecimal);
        loopsTyping = (entry.Id, text, false);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            loopsTyping = null;
            return;
        }

        if (!entered && !ImGui.IsItemDeactivated()) return;
        loopsTyping = null;
        if (PlaylistEditing.ParseLoops(text, out var loops) && loops != entry.Loops) Report(session.SetEntryLoops(entry.Id, loops));
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

    /// <summary>Accepts entries (to reorder) or Hierarchy tracks (to add) dropped on the last item, placing them at <paramref name="index"/>.</summary>
    private void DropTarget(Scene scene, int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;

        if (DragRows.Accept(DragRows.Entry) is { } entries && entries.Grabbed < scene.Playlist.Count)
            Report(session.MoveEntries(DragRows.Entries(session, scene, entries), scene.Playlist[entries.Grabbed].Id, index < scene.Playlist.Count ? scene.Playlist[index].Id : null));

        if (DragRows.Accept(DragRows.Track) is { } tracks) Report(session.AddToPlaylist(DragRows.Tracks(session, scene, tracks), index));

        ImGui.EndDragDropTarget();
    }
}
