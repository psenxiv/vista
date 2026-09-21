using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Editor;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CinematicCam.Plugin.Ui;

/// <summary>The selected point's number fields, gizmo mode and Delete; shown only while a point is selected in editing mode.</summary>
internal sealed class PointWindow : Window
{
    private const float FieldWidth = 70f;

    private readonly CameraSession session;
    private readonly PointGizmo gizmo;
    private readonly PendingField fields;
    private int? shown;

    public PointWindow(CameraSession session, PointGizmo gizmo, PendingField fields)
        : base("Point###ccam-point", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.session = session;
        this.gizmo = gizmo;
        this.fields = fields;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    /// <summary>Opens while a point is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var selected = session.Mode == CameraMode.Editing ? session.Selected : null;
        if (selected != shown) fields.Commit();
        shown = selected;
        IsOpen = selected is not null;
        if (selected is { } index) WindowName = $"Point {index + 1}###ccam-point";
    }

    /// <summary>Applies an unfinished field edit, since a closed window never reports the field losing focus.</summary>
    public override void OnClose() => fields.Commit();

    public override void Draw()
    {
        if (session.Selected is not { } index || index >= session.Track.Points.Count) return;
        var point = session.Track.Points[index];

        ImGui.TextUnformatted("Gizmo");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move", gizmo.Mode == GizmoMode.Move)) gizmo.SetMode(GizmoMode.Move);
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", gizmo.Mode == GizmoMode.Rotate)) gizmo.SetMode(GizmoMode.Rotate);

        Field("X", $"x{index}", point.Position.X, "%.1f", v => Edit(index, p => p with { Position = p.Position with { X = EditLimits.Coordinate(v, p.Position.X) } }));
        ImGui.SameLine();
        Field("Y", $"y{index}", point.Position.Y, "%.1f", v => Edit(index, p => p with { Position = p.Position with { Y = EditLimits.Coordinate(v, p.Position.Y) } }));
        ImGui.SameLine();
        Field("Z", $"z{index}", point.Position.Z, "%.1f", v => Edit(index, p => p with { Position = p.Position with { Z = EditLimits.Coordinate(v, p.Position.Z) } }));

        ImGui.BeginDisabled(session.Track.Aim == AimMode.PathTangent);
        Field("Yaw", $"yaw{index}", Degrees(EditLimits.Angle(point.Yaw)), "%.1f°", v => Edit(index, p => p with { Yaw = EditLimits.Angle(Radians(v)) }));
        ImGui.SameLine();
        Field("Pitch", $"pitch{index}", Degrees(point.Pitch), "%.1f°", v => Edit(index, p => p with { Pitch = EditLimits.Pitch(Radians(v)) }));
        ImGui.EndDisabled();

        Field("Roll", $"roll{index}", Degrees(EditLimits.Angle(point.Roll)), "%.1f°", v => Edit(index, p => p with { Roll = EditLimits.Angle(Radians(v)) }));
        ImGui.SameLine();
        Field("FoV", $"fov{index}", Degrees(point.Fov), "%.1f°", v => Edit(index, p => p with { Fov = ClampFov(Radians(v), p.Fov) }));

        if (ImGui.Button("Delete"))
        {
            fields.Clear();
            Report(session.DeleteSelected());
        }
    }

    private void Field(string label, string id, float value, string format, Action<float> apply)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        fields.Draw(id, value, format, FieldWidth, apply);
    }

    /// <summary>Replaces point <paramref name="index"/> with <paramref name="change"/> applied to it as it is now.</summary>
    private void Edit(int index, Func<ControlPoint, ControlPoint> change)
    {
        if (index >= session.Track.Points.Count) return;
        Report(session.ReplacePoint(index, change(session.Track.Points[index])));
    }

    /// <summary>A field of view within the game's range, or <paramref name="current"/> when the range cannot be read.</summary>
    private static float ClampFov(float radians, float current)
        => CameraAccess.ReadFovLimits() is { } limits ? EditLimits.Fov(radians, limits.Min, limits.Max) : current;

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
