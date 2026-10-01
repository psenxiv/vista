using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Ui.Widgets;
using Vista.Plugin.Ui.Windows;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The selected playlist: add, reorder, remove and set loop counts, and what Live is playing of it.</summary>
internal sealed class PlaylistPanel
{
    private const float LoopWidth = 44f;

    /// <summary>The width of the bar marking an entry that plays the edited track.</summary>
    private const float EditedBarWidth = 3f;

    private readonly SessionState session;
    private readonly FilePickerWindow picker;

    // A repeat count being dragged, applied when the field is let go; its field is the entry's id.
    private readonly PendingEdit<int> loopsDrag;

    // A repeat count being typed, and whether its field still needs focus.
    private (Guid Id, string Text, bool Focus)? loopsTyping;

    // Wheel travel over the loop cells, dropped when the mouse leaves them.
    private readonly WheelSteps wheel = new();
    private bool loopsHovered;

    public PlaylistPanel(SessionState session, FilePickerWindow picker)
    {
        this.session = session;
        this.picker = picker;
        loopsDrag = new PendingEdit<int>(() => session.Mode == CameraMode.Editing);
    }

    /// <summary>The header, naming the entry Live is playing, with its open, loop and add buttons, the selected playlist's name, then one row per entry; editing is disabled unless in Edit mode.</summary>
    public void Draw(bool editing)
    {
        // Rows can remove or reorder entries, so every row reads this snapshot.
        var scene = session.Scene;
        var playlist = PlaylistEditing.Selected(scene);
        var entries = playlist.Entries;
        var playing = session.PlayingEntry;
        if (loopsDrag.HeldBy is { } dragged && (!editing || PlaylistEditing.IndexOf(scene, Guid.Parse(dragged)) < 0))
            loopsDrag.Clear();
        if (loopsTyping is { } typed && (!editing || PlaylistEditing.IndexOf(scene, typed.Id) < 0))
            loopsTyping = null;
        loopsHovered = false;

        ImGui.AlignTextToFramePadding();
        var buttons = IconButton.RowWidth(FontAwesomeIcon.LayerGroup, FontAwesomeIcon.Repeat, FontAwesomeIcon.Plus);
        if (playing is not null && PlaylistHeading.NowPlaying(scene, playing.Id) is { } heading)
        {
            // Cut to the room left of the buttons, so a long name can't widen the panel.
            var room = ImGui.GetContentRegionAvail().X - buttons - ImGui.GetStyle().ItemSpacing.X;
            ImGui.TextUnformatted(RowFit.Ellipsis(heading, room, s => ImGui.CalcTextSize(s).X));
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
                ImGui.TextUnformatted("Playlist");
        }

        ImGui.BeginDisabled(!editing);
        ImGui.SameLine();
        Layout.RightAlign(buttons);
        if (IconButton.Draw("open-playlist", FontAwesomeIcon.LayerGroup, PlaylistPickerSource.OpenPlaylist))
            picker.Show(FilePickerKind.Playlist);
        ImGui.SameLine();
        if (IconButton.Toggle("playlist-loop", FontAwesomeIcon.Repeat, playlist.Loops, "Loop playlist"))
            Report(session.SetPlaylistLoops(!playlist.Loops));
        ImGui.SameLine();
        if (IconButton.Draw("add-entry", FontAwesomeIcon.Plus, "Add to playlist"))
            ImGui.OpenPopup("add-entry");
        if (ImGui.BeginPopup("add-entry"))
        {
            foreach (var track in scene.Tracks)
            {
                using var trackId = ImRaii.PushId(track.Id.ToString());
                if (ImGui.Selectable(track.Name))
                    Report(session.AddToPlaylist([track.Id]));
            }

            ImGui.EndPopup();
        }

        ImGui.EndDisabled();
        NameStrip.Draw("playlist-name", playlist.Name);
        ImGui.Separator();

        ImGui.BeginDisabled(!editing);
        if (ImGui.BeginChild("entries", new Vector2(0f, 0f)))
        {
            var held = false;
            var selected = session.Selection.Entries;
            var marked = session.Selection.EditedEntries;
            for (var i = 0; i < entries.Count; i++)
            {
                DrawRow(scene, entries[i], i, held, playing?.Id, selected, marked, editing);
                held |= PlaylistEditing.HoldsPlaylist(scene, entries[i]);
            }

            // An empty playlist's hint sits under the drop space, which starts back at its top so drops land on it too.
            if (entries.Count == 0)
            {
                var top = ImGui.GetCursorPos();
                Layout.CentredText("Drag tracks here, or click +.", UiColours.Dim());
                ImGui.SetCursorPos(top);
            }

            // The space under the rows takes dropped rows at the end.
            DragRows.Space(session, editing);
            DropTarget(scene, entries.Count, editing);
            DragRows.ScrollNearEdges(DragRows.Entry, DragRows.Track);
        }

        ImGui.EndChild();
        ImGui.EndDisabled();
        if (!loopsHovered)
            wheel.Reset();
    }

    /// <summary>One entry: its number and track, a warning when its watched or followed character isn't found, click to edit its track and select it, drag to reorder or drop tracks on it, right-click several for their menu, its loop cell and its remove button, shown on hover; highlighted while Live plays it, greyed when never reached, and barred at its left when <paramref name="marked"/> says it plays the edited track.</summary>
    private void DrawRow(
        Scene scene,
        PlaylistEntry entry,
        int index,
        bool unreachable,
        Guid? playing,
        IReadOnlyList<Guid> selected,
        IReadOnlyList<Guid> marked,
        bool editing
    )
    {
        using var id = ImRaii.PushId(entry.Id.ToString());
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f, unreachable);

        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var track = SceneEditing.Get(scene, entry.TrackId);
        var lost = session.World.TargetLost(session.World.WorldOf(track));
        var warning = lost ? IconButton.GlyphWidth(IconButton.WarningIcon) + gap : 0f;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f) - warning);
        var name = track.Name;
        var rowStart = ImGui.GetCursorPosX();
        var picked = selected.Contains(entry.Id);
        var group = RowPicking.IsGroup(selected, entry.Id);
        if (
            ImGui.Selectable(
                "##entry",
                editing ? picked : entry.Id == playing,
                ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())
            )
        )
            Report(session.Selection.ClickEntry(entry.Id, DragRows.Click()));
        if (marked.Contains(entry.Id))
        {
            var min = ImGui.GetItemRectMin();
            ImGui
                .GetWindowDrawList()
                .AddRectFilled(
                    min,
                    new Vector2(min.X + EditedBarWidth, ImGui.GetItemRectMax().Y),
                    ImGui.GetColorU32(UiColours.Accent)
                );
        }
        RowText.Draw(entry.Id, FormattableString.Invariant($"{index + 1}  {name}"), nameWidth);
        var rowHovered = editing && IconButton.RowHovered(ImGui.GetItemRectMin(), ImGui.GetItemRectMax().Y);

        if (editing)
            DragRows.Source(DragRows.Entry, index, group, selected.Count, "rows", name);

        DropTarget(scene, index, editing);
        if (editing && group && ImGui.BeginPopupContextItem("entry-menu"))
        {
            if (Menu.Item("Remove from playlist"))
                Report(session.RemoveFromPlaylist(selected));
            ImGui.EndPopup();
        }

        // The selectable spans the row, so the row's other items go back over it.
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + nameWidth + gap);
        if (lost)
        {
            IconButton.TargetNotFound(
                track.Aim == AimMode.FollowTarget ? IconButton.FollowNotFoundTooltip : IconButton.NotFoundTooltip
            );
            ImGui.SameLine();
        }

        DrawLoops(scene, entry, editing);

        ImGui.SameLine();
        if (IconButton.RowAction("remove", FontAwesomeIcon.Times, "Remove from playlist", rowHovered, danger: true))
            Report(session.RemoveFromPlaylist([entry.Id]));
    }

    /// <summary>The repeat count as a drag field: 1 up, or 0 to follow the track, shown as ∞ when that holds the playlist or — when it plays once. Double-click or Ctrl + click to type, wheel to step; a drag applies when let go.</summary>
    private void DrawLoops(Scene scene, PlaylistEntry entry, bool editing)
    {
        if (loopsTyping is { } typing && typing.Id == entry.Id)
        {
            DrawLoopsText(scene, entry, typing);
            return;
        }

        var holds = PlaylistEditing.HoldsPlaylist(scene, entry);
        loopsDrag.Draw(
            entry.Id.ToString(),
            entry.Loops ?? 0,
            (ref int value) => DrawLoopCount(ref value, holds),
            loops => Report(session.SetEntryLoops(entry.Id, loops > 0 ? loops : null))
        );
        Tooltip.OnHover("Repeats");

        // ImGui's own typing reads the text through the display format, which has no number when it shows — or ∞.
        if (
            editing
            && ImGui.IsItemHovered()
            && (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) || (ImGui.IsItemClicked() && ImGui.GetIO().KeyCtrl))
        )
        {
            loopsDrag.Clear();
            if (loopsTyping is { } open)
                ApplyTyped(scene, open.Id, open.Text);
            loopsTyping = (entry.Id, entry.Loops?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, true);
            return;
        }

        if (editing)
            ImGuiP.SetItemUsingMouseWheel();
        if (!editing || !ImGui.IsItemHovered())
            return;
        loopsHovered = true;
        StepLoops(entry);
    }

    /// <summary>The repeat count's drag field showing <paramref name="value"/>: amber, or dimmed when the entry plays once; true when dragged.</summary>
    private static bool DrawLoopCount(ref int value, bool holds)
    {
        var repeats = PlaylistEditing.RepeatsOf(value, holds);
        var format = repeats switch
        {
            Repeats.Count => "%d",
            Repeats.Forever => "∞",
            _ => "—",
        };
        var colour = repeats == Repeats.Once ? UiColours.Dim() : UiColours.Amber;

        ImGui.SetNextItemWidth(LoopWidth);
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
            return ImGui.DragInt(
                "##loops",
                ref value,
                0.1f,
                0,
                PlaylistEditing.MaxLoops,
                format,
                ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput
            );
    }

    /// <summary>The repeat count as text: Enter or clicking away applies it, blank or 0 follows the track, Escape cancels.</summary>
    private void DrawLoopsText(Scene scene, PlaylistEntry entry, (Guid Id, string Text, bool Focus) typing)
    {
        var text = typing.Text;
        var result = TextEdit.Draw(
            "##loops-text",
            ref text,
            8,
            LoopWidth,
            typing.Focus,
            ImGuiInputTextFlags.CharsDecimal
        );
        loopsTyping = result == TextEdit.Result.Editing ? (entry.Id, text, false) : null;
        if (result == TextEdit.Result.Apply)
            ApplyTyped(scene, entry.Id, text);
    }

    /// <summary>Sets entry <paramref name="id"/>'s repeat count from typed <paramref name="text"/>, when it reads as a count and changes it.</summary>
    private void ApplyTyped(Scene scene, Guid id, string text)
    {
        var index = PlaylistEditing.IndexOf(scene, id);
        if (
            index >= 0
            && PlaylistEditing.ParseLoops(text, out var loops)
            && loops != PlaylistEditing.Selected(scene).Entries[index].Loops
        )
            Report(session.SetEntryLoops(id, loops));
    }

    /// <summary>Each whole notch of the mouse wheel steps the count by one: down from 1 empties it, up from empty gives 1.</summary>
    private void StepLoops(PlaylistEntry entry)
    {
        var steps = wheel.Take(ImGui.GetIO().MouseWheel);
        var up = steps > 0;
        var loops = entry.Loops;
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            var next = PlaylistEditing.StepLoops(loops, up);
            if (next == loops)
                continue;
            Report(session.SetEntryLoops(entry.Id, next));
            loops = next;
        }
    }

    /// <summary>Accepts entries (to reorder) or Hierarchy tracks (to add) dropped on the last item, placing them at <paramref name="index"/>.</summary>
    private void DropTarget(Scene scene, int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget())
            return;

        var rows = PlaylistEditing.Selected(scene).Entries;
        if (DragRows.Accept(DragRows.Entry) is { } entries && entries.Grabbed < rows.Count)
            Report(
                session.MoveEntries(
                    DragRows.Entries(session, scene, entries),
                    rows[entries.Grabbed].Id,
                    index < rows.Count ? rows[index].Id : null
                )
            );

        if (DragRows.Accept(DragRows.Track) is { } tracks)
            Report(session.AddToPlaylist(DragRows.Tracks(session, scene, tracks), index));

        ImGui.EndDragDropTarget();
    }
}
