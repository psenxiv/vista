using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Input;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The edited track's points: one row per point and the leg arriving at it, with its header pinned above the rows.</summary>
internal sealed class PointList
{
    private static readonly Vector2 CellPadding = new(6f, 0f);

    // The space above and below a points row's contents, given as row height so value cells can fill the whole row.
    private const float RowPadding = 4f;
    private const string NumberHeader = "#";
    private static readonly string[] ValueHeaders = ["Duration (s)", "Speed", "Hold (s)"];

    private static readonly PendingField.Range LegRange = new(
        0.05f,
        TrackEditing.MinLegSeconds,
        TrackEditing.MaxSeconds
    );
    private static readonly PendingField.Range HoldRange = new(0.05f, 0f, TrackEditing.MaxSeconds);

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly PendingEdit<float> fields;

    // Whether the points list scrolled last frame, so its width can leave room for the scrollbar's inset.
    private bool pointsScroll;

    public PointList(GameSession game, PendingEdit<float> fields)
    {
        this.game = game;
        session = game.State;
        this.fields = fields;
    }

    public void Draw(bool editing)
    {
        // Rows can delete or reorder points, so every row reads this snapshot.
        var track = session.Track;
        var evaluator = session.World.Evaluator;
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        var headerHeight = HeaderRowHeight();
        var top = ImGui.GetCursorPos();
        var height = ImGui.GetContentRegionAvail().Y - footer;

        // The header table is drawn last, at the reserved position above, once the rows table's own width is known.
        ImGui.SetCursorPosY(top.Y + headerHeight);
        var rowsWidth = 0f;
        if (ImGui.BeginChild("points", new Vector2(PointsWidth(), height - headerHeight)))
        {
            pointsScroll = ImGui.GetScrollMaxY() > 0f;
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, CellPadding);
            // Measured here so a scrollbar this child reserves narrows the header to match.
            rowsWidth = ImGui.GetContentRegionAvail().X;
            if (
                ImGui.BeginTable(
                    "point-table",
                    7,
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX,
                    new Vector2(rowsWidth, 0f)
                )
            )
            {
                SetupPointColumns();

                var selected = session.Selection.Points;
                for (var i = 0; i < track.Points.Count; i++)
                    DrawPointRow(track, evaluator, i, selected, editing);
                ImGui.EndTable();
            }

            // An empty track's hint sits under the point space, which starts back at its top so it still takes clicks and drops.
            if (track.Points.Count == 0)
            {
                var hintTop = ImGui.GetCursorPos();
                Layout.CentredText(
                    $"Press {HotkeyTable.AddToEnd.Hotkey.DisplayName} or + to add points.",
                    UiColours.Dim()
                );
                ImGui.SetCursorPos(hintTop);
            }

            DrawPointSpace(track, editing);
            DragRows.ScrollNearEdges(DragRows.Point);
        }

        ImGui.EndChild();
        var bottom = ImGui.GetCursorPos();

        if (rowsWidth > 0f)
        {
            ImGui.SetCursorPos(top);
            DrawPointHeaderTable(rowsWidth);
        }

        ImGui.SetCursorPos(bottom);
    }

    /// <summary>The points list's width, leaving the same margin on the right as on the left, scrollbar included.</summary>
    private float PointsWidth()
    {
        var left = ImGui.GetWindowPos().X;
        var start = ImGui.GetCursorScreenPos().X;
        var right = left + ImGui.GetWindowSize().X - (start - left);
        // ImGui insets a scrollbar's grab from its track by up to 3 pixels.
        var inset = pointsScroll ? MathF.Min(3f, MathF.Floor((ImGui.GetStyle().ScrollbarSize - 2f) * 0.5f)) : 0f;
        return right + inset - start;
    }

    /// <summary>The header table pinned above the scrolling rows: given the rows table's own width so their columns line up exactly, scrollbar included.</summary>
    private static void DrawPointHeaderTable(float width)
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, CellPadding);
        if (
            ImGui.BeginTable(
                "point-table-header",
                7,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX,
                new Vector2(width, 0f)
            )
        )
        {
            SetupPointColumns();
            DrawPointHeaders();
            ImGui.EndTable();
        }
    }

    /// <summary>The point table's seven columns, set up identically for the header table and the rows table so their columns line up.</summary>
    private static void SetupPointColumns()
    {
        ImGui.TableSetupColumn(
            "##handle",
            ImGuiTableColumnFlags.WidthFixed,
            IconButton.GlyphWidth(FontAwesomeIcon.GripVertical)
        );
        // Fixed, rather than auto-fit, so a separate header table's # column can be given the same width.
        ImGui.TableSetupColumn(
            NumberHeader,
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
            MathF.Max(ImGui.CalcTextSize(NumberHeader).X, ImGui.CalcTextSize("000").X)
        );
        foreach (var header in ValueHeaders)
            SetupValueColumn(header);
        ImGui.TableSetupColumn("##pin", ImGuiTableColumnFlags.WidthFixed, IconButton.Width(FontAwesomeIcon.Thumbtack));
        ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthStretch);
    }

    /// <summary>A value column, fixed at the field width or its header's, whichever is wider, so a cell-filling field can't widen it.</summary>
    private static void SetupValueColumn(string header) =>
        ImGui.TableSetupColumn(
            header,
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
            MathF.Max(Layout.FieldWidth, ImGui.CalcTextSize(header).X)
        );

    /// <summary>A header row's height: one text line plus the padding used for every point row.</summary>
    private static float HeaderRowHeight() => ImGui.GetTextLineHeight() + (RowPadding * 2f);

    /// <summary>The header row, drawn as text so it keeps its padding while the table's cells have none.</summary>
    private static void DrawPointHeaders()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, HeaderRowHeight());
        ImGui.TableSetColumnIndex(1);
        PadRow();
        ImGui.TextUnformatted(NumberHeader);
        for (var i = 0; i < ValueHeaders.Length; i++)
        {
            ImGui.TableSetColumnIndex(2 + i);
            PadRow();
            ImGui.TextUnformatted(ValueHeaders[i]);
        }
    }

    /// <summary>Moves the cursor down by the row padding, for a cell whose contents don't fill the row.</summary>
    private static void PadRow() => ImGui.SetCursorPosY(ImGui.GetCursorPosY() + RowPadding);

    /// <summary>One point and the leg arriving at it: the whole row selects on click, jumps on double-click, drags to reorder or onto a track, and right-clicks for its menu; the trash icon, shown on hover, deletes it.</summary>
    private void DrawPointRow(
        Track track,
        TrackEvaluator evaluator,
        int index,
        IReadOnlyList<int> selected,
        bool editing
    )
    {
        var rowHeight = ImGui.GetFrameHeight() + (RowPadding * 2f);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        ImGui.BeginDisabled(!editing);

        ImGui.TableNextColumn();
        // Selectable pads itself by half the item spacing above and below, so it's drawn that much inside the row to cover exactly the row.
        var rowTop = ImGui.GetCursorPosY();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        ImGui.SetCursorPosY(rowTop + MathF.Floor(spacing * 0.5f));
        var rowFlags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap;
        var picked = selected.Contains(index);
        var group = RowPicking.IsGroup(selected, index);
        if (ImGui.Selectable($"##row{index}", picked, rowFlags, new Vector2(0f, rowHeight - spacing)))
            session.Selection.ClickPoint(index, DragRows.Click());
        var rowHovered = editing && IconButton.RowHovered(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            game.JumpToPoint(index);
        if (editing)
            DragRows.Source(
                DragRows.Point,
                index,
                group,
                selected.Count,
                "points",
                FormattableString.Invariant($"Point {index + 1}")
            );

        DropTarget(index, editing);
        if (editing && ImGui.BeginPopupContextItem($"point-menu{index}"))
        {
            DrawPointMenu(group ? selected : [index]);
            ImGui.EndPopup();
        }

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorPosY(rowTop + RowPadding);
        ImGui.AlignTextToFramePadding();
        IconButton.Glyph(FontAwesomeIcon.GripVertical, rowHovered ? null : UiColours.Dim());

        ImGui.TableNextColumn();
        PadRow();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted((index + 1).ToString(CultureInfo.InvariantCulture));

        ImGui.TableNextColumn();
        if (index > 0)
            DrawValueCell(
                index,
                $"leg{index}",
                evaluator.LegSeconds(index),
                Units.SecondsNumber,
                LegRange,
                v => Report(session.SetLegDuration(index, v)),
                rowHovered
            );

        ImGui.TableNextColumn();
        if (index > 0)
            DrawValueCell(
                index,
                $"leg-speed{index}",
                evaluator.LegLength(index) / evaluator.LegSeconds(index),
                Units.YalmsPerSecondField,
                TrackEditorWindow.SpeedRange,
                v => Report(session.SetLegSpeed(index, v)),
                rowHovered
            );

        ImGui.TableNextColumn();
        DrawValueCell(
            index,
            $"hold{index}",
            TrackEditing.HoldSeconds(track, index),
            Units.SecondsNumber,
            HoldRange,
            v => Report(session.ChangeTrack(t => TrackEditing.SetHold(t, index, v))),
            rowHovered
        );

        ImGui.TableNextColumn();
        PadRow();
        if (index > 0)
            DrawPin(track, index, rowHovered);

        ImGui.TableNextColumn();
        DrawDeleteCell(index, rowHovered);

        ImGui.EndDisabled();
    }

    /// <summary>The row's trash icon, right-aligned, which deletes only this point.</summary>
    private void DrawDeleteCell(int index, bool rowHovered)
    {
        PadRow();
        Layout.RightAlign(IconButton.Width(FontAwesomeIcon.Trash));
        if (
            IconButton.RowAction(
                $"delete{index}",
                FontAwesomeIcon.Trash,
                HotkeyTable.DeleteSelectedPoints.Hotkey.Tooltip("Delete point"),
                rowHovered,
                danger: true
            )
        )
        {
            fields.Clear();
            Report(session.DeletePoints([index]));
        }
    }

    /// <summary>Point <paramref name="index"/>'s value field, filling its cell and highlighted while the row is hovered; a click also selects the row.</summary>
    private void DrawValueCell(
        int index,
        string id,
        float current,
        string format,
        PendingField.Range range,
        Action<float> apply,
        bool rowHovered
    )
    {
        var width = ImGui.GetContentRegionAvail().X + (CellPadding.X * 2f);
        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() - new Vector2(CellPadding.X, 0f));
        var framePadding = ImGui.GetStyle().FramePadding;
        using var style = ImRaii
            .PushStyle(ImGuiStyleVar.FrameRounding, 0f)
            .Push(ImGuiStyleVar.FramePadding, framePadding with { Y = framePadding.Y + RowPadding });
        using var colour = ImRaii.PushColor(ImGuiCol.FrameBg, rowHovered ? UiColours.FrameHint() : 0u);
        var click = DragRows.Click();
        fields.Draw(
            id,
            current,
            format,
            width,
            range,
            apply,
            click == RowClick.Plain ? ImGuiSliderFlags.None : ImGuiSliderFlags.NoInput
        );
        if (ImGui.IsItemClicked())
            session.Selection.ClickPoint(index, click);
    }

    /// <summary>The leg's pin: always shown in the accent colour when pinned, shown only on row hover when following the track speed.</summary>
    private void DrawPin(Track track, int index, bool rowHovered)
    {
        if (TrackEditing.IsPinned(track, index))
        {
            if (IconButton.Draw($"pin{index}", FontAwesomeIcon.Thumbtack, "Pin to Track speed", UiColours.Accent))
                Report(session.ResetLeg(index));
            return;
        }

        if (IconButton.RowAction($"pin{index}", FontAwesomeIcon.Thumbtack, "Pin to Leg speed", rowHovered))
            Report(session.SetLegSpeed(index, TrackEditing.LegSpeed(track, index)));
    }

    /// <summary>Moves the dragged points to <paramref name="index"/>, or the end when null, when they are dropped on this item.</summary>
    private void DropTarget(int? index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget())
            return;
        if (DragRows.Accept(DragRows.Point) is { } points)
            Report(session.MovePoints(DragRows.Points(session, points), points.Grabbed, index));
        ImGui.EndDragDropTarget();
    }

    /// <summary>The menu for points: move them to a new track or another one, duplicate them, or delete them.</summary>
    private void DrawPointMenu(IReadOnlyList<int> points)
    {
        var scene = session.Scene;
        if (Menu.Item("Move to new track"))
            Report(session.MovePointsTo(points, null));
        if (ImGui.BeginMenu("Move to", scene.Tracks.Count > 1))
        {
            foreach (var other in scene.Tracks.Where(t => t.Id != session.EditedTrackId))
            {
                using var id = ImRaii.PushId(other.Id.ToString());
                if (Menu.Item(other.Name, PointTransfer.CanTake(other, session.EditedTrackId)))
                    Report(session.MovePointsTo(points, other.Id));
            }

            ImGui.EndMenu();
        }

        if (Menu.Item("Duplicate", TrackEditing.CanDuplicate(session.Track)))
        {
            fields.Commit();
            Report(session.DuplicatePoints(points));
        }

        // The shortcut deletes Selection.Points, so it only belongs on the menu when its points are that selection.
        var shortcut = points.SequenceEqual(session.Selection.Points)
            ? HotkeyTable.DeleteSelectedPoints.Hotkey.DisplayName
            : "";
        if (Menu.Item("Delete", shortcut: shortcut))
        {
            fields.Clear();
            Report(session.DeletePoints(points));
        }
    }

    /// <summary>The space under the points: it takes dropped points at the end, and a click there clears the selection.</summary>
    private void DrawPointSpace(Track track, bool editing)
    {
        DragRows.Space(session, editing);
        if (track.Points.Count > 0)
            DropTarget(null, editing);
    }
}
