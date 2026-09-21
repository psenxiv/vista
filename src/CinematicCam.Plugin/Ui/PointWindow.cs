using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Editor;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The selected point's number fields, gizmo mode, copy, paste and delete; shown only while a point is selected in editing mode.</summary>
internal sealed class PointWindow : Window
{
    private const float FieldWidth = 70f;
    private const float PositionSpeed = 0.02f;
    private const float AngleSpeed = 0.25f;
    private const float FovSpeed = 0.1f;

    private readonly CameraSession session;
    private readonly PointGizmo gizmo;
    private int? shown;
    private float fieldsWidth;
    private ControlPoint? copied;

    public PointWindow(CameraSession session, PointGizmo gizmo)
        : base("Point###ccam-point", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.gizmo = gizmo;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    /// <summary>Opens while a point is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var selected = session.Mode == CameraMode.Editing ? session.Selected : null;
        if (selected != shown) session.EndPointEdit();
        shown = selected;
        IsOpen = selected is not null;
        if (selected is { } index) WindowName = $"Point {index + 1}###ccam-point";
    }

    /// <summary>Ends a drag in progress, since a closed window never reports the field letting go.</summary>
    public override void OnClose() => session.EndPointEdit();

    public override void Draw()
    {
        if (session.Selected is not { } index || index >= session.Track.Points.Count) return;
        var point = session.Track.Points[index];
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));

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
        if (IconButton.Draw("copy-point", FontAwesomeIcon.Copy, "Copy position, aim, roll and FoV")) copied = point;

        ImGui.SameLine();
        ImGui.BeginDisabled(copied is null);
        if (IconButton.Draw("paste-point", FontAwesomeIcon.Paste, "Paste position, aim, roll and FoV") && copied is { } c)
            Report(session.ReplacePoint(index, point with { Position = c.Position, Yaw = c.Yaw, Pitch = c.Pitch, Roll = c.Roll, Fov = c.Fov }));
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (IconButton.Draw("delete-point", FontAwesomeIcon.Trash, "Delete point"))
        {
            Report(session.DeleteSelected());
            return;
        }

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f));
        if (!ImGui.BeginTable("point-fields", 6, ImGuiTableFlags.SizingFixedFit)) return;

        ImGui.TableNextRow();
        Field("X", EditorColours.AxisX, $"x{index}", index, point.Position.X, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { X = EditLimits.Coordinate(v, p.Position.X) } });
        Field("Y", EditorColours.AxisY, $"y{index}", index, point.Position.Y, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { Y = EditLimits.Coordinate(v, p.Position.Y) } });
        Field("Z", EditorColours.AxisZ, $"z{index}", index, point.Position.Z, PositionSpeed, "%.2f", (p, v) => p with { Position = p.Position with { Z = EditLimits.Coordinate(v, p.Position.Z) } });

        // Pitch turns about X, yaw about Y and roll about Z, so each sits under its axis in the gizmo's colours.
        ImGui.TableNextRow();
        ImGui.BeginDisabled(session.Track.Aim == AimMode.PathTangent);
        Field("Pitch", EditorColours.AxisX, $"pitch{index}", index, Degrees(point.Pitch), AngleSpeed, "%.1f°", (p, v) => p with { Pitch = EditLimits.Pitch(Radians(v)) });
        Field("Yaw", EditorColours.AxisY, $"yaw{index}", index, Degrees(EditLimits.Angle(point.Yaw)), AngleSpeed, "%.1f°", (p, v) => p with { Yaw = EditLimits.Angle(Radians(v)) });
        ImGui.EndDisabled();
        Field("Roll", EditorColours.AxisZ, $"roll{index}", index, Degrees(EditLimits.Angle(point.Roll)), AngleSpeed, "%.1f°", (p, v) => p with { Roll = EditLimits.Angle(Radians(v)) });

        ImGui.TableNextRow();
        Field("FoV", null, $"fov{index}", index, Degrees(point.Fov), FovSpeed, "%.1f°", (p, v) => p with { Fov = EditLimits.Fov(Radians(v)) });

        ImGui.EndTable();
        fieldsWidth = ImGui.GetItemRectSize().X;
    }

    /// <summary>A label and a drag field, as two cells of the grid: dragging moves the point live, and each drag is one undo step.</summary>
    private void Field(string label, uint? colour, string id, int index, float value, float speed, string format, Func<ControlPoint, float, ControlPoint> set)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(FieldWidth);

        var edited = value;
        var changed = ImGui.DragFloat($"##{id}", ref edited, speed, 0f, 0f, format);
        if (ImGui.IsItemActivated()) session.BeginPointEdit();
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        if (changed && index < session.Track.Points.Count) _ = session.PreviewPoint(index, set(session.Track.Points[index], edited));
        if (ImGui.IsItemDeactivated()) session.EndPointEdit();
    }

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
