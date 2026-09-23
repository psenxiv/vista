using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The free camera's own position, aim, roll and field of view, in the Point window's layout; shown only while editing.</summary>
internal sealed class CameraWindow : Window
{
    private readonly CameraSession session;
    private float gridWidth;

    public CameraWindow(CameraSession session) : base("Camera###vista-camera")
    {
        this.session = session;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Open only while editing, since the free camera exists only then.</summary>
    public override bool DrawConditions() => session.Mode == CameraMode.Editing;

    public override void Draw()
    {
        using var style = PoseGrid.Style();

        // The camera has no gizmo and nothing to copy, paste or delete; the row stays so the windows match.
        _ = PoseGrid.Header(gizmo: null, rotates: false, gridWidth, canCopy: false, canPaste: false, canDelete: false);
        if (!PoseGrid.BeginGrid()) return;

        var position = session.CameraPosition;
        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.ArrowsAlt, "Position");
        Field("cam-x", "X", EditorColours.AxisX, position.X, PoseGrid.PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { X = EditLimits.Coordinate(v, position.X) });
        Field("cam-y", "Y", EditorColours.AxisY, position.Y, PoseGrid.PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { Y = EditLimits.Coordinate(v, position.Y) });
        Field("cam-z", "Z", EditorColours.AxisZ, position.Z, PoseGrid.PositionSpeed, "%.2f",
            v => session.CameraPosition = position with { Z = EditLimits.Coordinate(v, position.Z) });

        var (yaw, pitch) = session.CameraAngles ?? (0f, 0f);
        ImGui.TableNextRow();
        if (PoseGrid.Button("level-roll", FontAwesomeIcon.SyncAlt, "Level roll", enabled: true)) session.CameraRoll = 0f;
        Field("cam-pitch", "Pitch", EditorColours.AxisX, PoseGrid.Degrees(pitch), PoseGrid.AngleSpeed, "%.1f°",
            v => session.TurnCamera(yaw, EditLimits.Pitch(PoseGrid.Radians(v))));
        Field("cam-yaw", "Yaw", EditorColours.AxisY, PoseGrid.Degrees(EditLimits.Angle(yaw)), PoseGrid.AngleSpeed, "%.1f°",
            v => session.TurnCamera(EditLimits.Angle(PoseGrid.Radians(v)), pitch));
        Field("cam-roll", "Roll", EditorColours.AxisZ, PoseGrid.Degrees(session.CameraRoll), PoseGrid.AngleSpeed, "%.1f°",
            v => session.CameraRoll = EditLimits.Angle(PoseGrid.Radians(v)));

        ImGui.TableNextRow();
        var takeover = session.TakeoverFov;
        if (PoseGrid.Button("reset-fov", FontAwesomeIcon.History, "Reset to the game's field of view", enabled: takeover is not null) && takeover is { } fov)
            session.CameraFov = fov;
        Field("cam-fov", "FoV", null, PoseGrid.Degrees(session.CameraFov), PoseGrid.FovSpeed, "%.1f°",
            v => session.CameraFov = EditLimits.Fov(PoseGrid.Radians(v)));

        gridWidth = PoseGrid.EndGrid();
    }

    /// <summary>A field that writes straight to the camera. Flying is not scene state, so this is not an undo step.</summary>
    private static void Field(string id, string name, uint? border, float value, float speed, string format, Action<float> set)
    {
        var edited = value;
        if (PoseGrid.Field(id, name, border, ref edited, speed, format)) set(edited);
    }
}
