using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The selected point's or anchor's number fields and gizmo mode; shown only while one is selected in editing mode.</summary>
internal sealed class PointWindow : Window
{
    private const float FieldWidth = 70f;
    private const float PositionSpeed = 0.02f;
    private const float AngleSpeed = 0.25f;
    private const float FovSpeed = 0.1f;

    private readonly CameraSession session;
    private readonly PointGizmo gizmo;
    private (int? Point, AnchorKind? Anchor, Guid Track) shown;
    private float fieldsWidth;
    private ControlPoint? copied;

    public PointWindow(CameraSession session, PointGizmo gizmo)
        : base("Point###vista-point", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.gizmo = gizmo;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    /// <summary>Opens while a point or an anchor is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var editing = session.Mode == CameraMode.Editing;
        var now = (editing ? session.Selected : null, editing ? session.SelectedAnchor : null, session.EditedTrackId);
        if (now != shown) session.EndLiveEdit();
        shown = now;
        IsOpen = now.Item1 is not null || now.Item2 is not null;
        WindowName = now switch
        {
            (_, AnchorKind.Scene, _) => "Scene anchor###vista-point",
            (_, AnchorKind.Track, _) => "Track anchor###vista-point",
            ({ } index, _, _) => $"Point {index + 1}###vista-point",
            _ => WindowName,
        };
    }

    /// <summary>Ends a drag in progress, since a closed window never reports the field letting go.</summary>
    public override void OnClose() => session.EndLiveEdit();

    public override void Draw()
    {
        Anchor? anchor = null;
        var index = -1;
        if (session.SelectedAnchor is not null)
        {
            if (session.SelectedAnchorInWorld is not { } a) return;
            anchor = a;
        }
        else if (session.Selected is { } i && i < session.Track.Points.Count) index = i;
        else return;

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));
        if (!DrawHeader(anchor is null ? index : null)) return;

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f));
        if (!ImGui.BeginTable("fields", 4, ImGuiTableFlags.SizingFixedFit)) return;

        if (anchor is { } shownAnchor) DrawAnchorRows(shownAnchor);
        else DrawPointRows(index, session.Track.Points[index]);

        ImGui.EndTable();
        fieldsWidth = ImGui.GetItemRectSize().X;
    }

    /// <summary>Gizmo mode, then copy, paste and delete, disabled for an anchor; false once the point is deleted.</summary>
    private bool DrawHeader(int? pointIndex)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Gizmo");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move", gizmo.Mode == GizmoMode.Move)) gizmo.SetMode(GizmoMode.Move);
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", gizmo.Mode == GizmoMode.Rotate)) gizmo.SetMode(GizmoMode.Rotate);

        // Right-align to last frame's field grid, not the window: the window sizes itself to its content.
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var icons = IconButton.Width(FontAwesomeIcon.Copy) + IconButton.Width(FontAwesomeIcon.Paste) + IconButton.Width(FontAwesomeIcon.Trash) + (gap * 2f);
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetStyle().WindowPadding.X + fieldsWidth - icons));

        ImGui.BeginDisabled(pointIndex is null);
        var point = pointIndex is { } i ? session.Track.Points[i] : (ControlPoint?)null;
        if (IconButton.Draw("copy-point", FontAwesomeIcon.Copy, "Copy position, aim, roll and FoV") && point is { } source) copied = source;

        ImGui.SameLine();
        ImGui.BeginDisabled(copied is null);
        if (IconButton.Draw("paste-point", FontAwesomeIcon.Paste, "Paste position, aim, roll and FoV") && copied is { } c && pointIndex is { } target && point is { } p)
            Report(session.ReplacePoint(target, p with { Position = c.Position, Yaw = c.Yaw, Pitch = c.Pitch, Roll = c.Roll, Fov = c.Fov }));
        ImGui.EndDisabled();

        ImGui.SameLine();
        var deleted = IconButton.Draw("delete-point", FontAwesomeIcon.Trash, "Delete point", danger: true) && pointIndex is not null;
        ImGui.EndDisabled();
        if (deleted) Report(session.DeleteSelected());
        return !deleted;
    }

    /// <summary>The point's position, rotation and FoV rows; pitch and yaw are disabled under direction of travel.</summary>
    private void DrawPointRows(int index, ControlPoint point)
    {
        ImGui.TableNextRow();
        PointField($"x{index}", "X", EditorColours.AxisX, index, point.Position.X, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { X = EditLimits.Coordinate(v, p.Position.X) } });
        PointField($"y{index}", "Y", EditorColours.AxisY, index, point.Position.Y, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { Y = EditLimits.Coordinate(v, p.Position.Y) } });
        PointField($"z{index}", "Z", EditorColours.AxisZ, index, point.Position.Z, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { Z = EditLimits.Coordinate(v, p.Position.Z) } });
        RowIcon(FontAwesomeIcon.ArrowsAlt, "Position");

        ImGui.TableNextRow();
        ImGui.BeginDisabled(session.Track.Aim == AimMode.PathTangent);
        PointField($"pitch{index}", "Pitch", EditorColours.AxisX, index, Degrees(point.Pitch), AngleSpeed, "%.1f°", (p, v) => p with { Pitch = EditLimits.Pitch(Radians(v)) });
        PointField($"yaw{index}", "Yaw", EditorColours.AxisY, index, Degrees(EditLimits.Angle(point.Yaw)), AngleSpeed, "%.1f°", (p, v) => p with { Yaw = EditLimits.Angle(Radians(v)) });
        ImGui.EndDisabled();
        PointField($"roll{index}", "Roll", EditorColours.AxisZ, index, Degrees(EditLimits.Angle(point.Roll)), AngleSpeed, "%.1f°", (p, v) => p with { Roll = EditLimits.Angle(Radians(v)) });
        RowIcon(FontAwesomeIcon.SyncAlt, "Rotation");

        ImGui.TableNextRow();
        PointField($"fov{index}", "FoV", null, index, Degrees(point.Fov), FovSpeed, "%.1f°", (p, v) => p with { Fov = EditLimits.Fov(Radians(v)) });
    }

    /// <summary>The anchor's rows: X, Y, Z and Yaw edit it, and the fields it lacks are disabled.</summary>
    private void DrawAnchorRows(Anchor anchor)
    {
        ImGui.TableNextRow();
        AnchorField("anchor-x", "X", EditorColours.AxisX, anchor.Position.X, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { X = EditLimits.Coordinate(v, a.Position.X) } });
        AnchorField("anchor-y", "Y", EditorColours.AxisY, anchor.Position.Y, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { Y = EditLimits.Coordinate(v, a.Position.Y) } });
        AnchorField("anchor-z", "Z", EditorColours.AxisZ, anchor.Position.Z, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { Z = EditLimits.Coordinate(v, a.Position.Z) } });
        RowIcon(FontAwesomeIcon.ArrowsAlt, "Position");

        ImGui.TableNextRow();
        MissingField("anchor-pitch", "Pitch", EditorColours.AxisX);
        AnchorField("anchor-yaw", "Yaw", EditorColours.AxisY, Degrees(EditLimits.Angle(anchor.Yaw)), AngleSpeed, "%.1f°", (a, v) => a with { Yaw = EditLimits.Angle(Radians(v)) });
        MissingField("anchor-roll", "Roll", EditorColours.AxisZ);
        RowIcon(FontAwesomeIcon.SyncAlt, "Rotation");

        ImGui.TableNextRow();
        MissingField("anchor-fov", "FoV", null);
    }

    /// <summary>A point's field: dragging moves the point live, and each drag is one undo step.</summary>
    private void PointField(string id, string name, uint? border, int index, float value, float speed, string format, Func<ControlPoint, float, ControlPoint> set)
    {
        var edited = value;
        var changed = BorderedField(id, name, border, ref edited, speed, format);
        if (ImGui.IsItemActivated()) session.BeginLiveEdit();
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        if (changed && index < session.Track.Points.Count) _ = session.PreviewPoint(index, set(session.Track.Points[index], edited));
        if (ImGui.IsItemDeactivated()) session.EndLiveEdit();
    }

    /// <summary>An anchor's field: dragging moves it live, carrying what hangs off it, and each drag is one undo step.</summary>
    private void AnchorField(string id, string name, uint border, float value, float speed, string format, Func<Anchor, float, Anchor> set)
    {
        var edited = value;
        var changed = BorderedField(id, name, border, ref edited, speed, format);
        if (ImGui.IsItemActivated()) session.BeginLiveEdit();
        if (changed && session.SelectedAnchorInWorld is { } current) _ = session.PreviewAnchor(set(current, edited), carry: true);
        if (ImGui.IsItemDeactivated()) session.EndLiveEdit();
    }

    /// <summary>A disabled field showing "—", for what an anchor does not have.</summary>
    private static void MissingField(string id, string name, uint? border)
    {
        var none = 0f;
        ImGui.BeginDisabled(true);
        _ = BorderedField(id, name, border, ref none, 0f, "—");
        ImGui.EndDisabled();
    }

    /// <summary>One number field: bordered in <paramref name="border"/>, named by its tooltip, and dragged live as one undo step.</summary>
    private static bool BorderedField(string id, string name, uint? border, ref float value, float speed, string format)
    {
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(FieldWidth);
        bool changed;
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f, border is not null))
        using (ImRaii.PushColor(ImGuiCol.Border, border ?? 0u, border is not null))
            changed = ImGui.DragFloat($"##{id}", ref value, speed, 0f, 0f, format);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(name);
        return changed;
    }

    /// <summary>The row's icon at its right end, naming the row in its tooltip.</summary>
    private static void RowIcon(FontAwesomeIcon icon, string name)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted(icon.ToIconString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
    }

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
